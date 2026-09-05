using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.Options;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Subscriptions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Subscriptions;

public class ScanReservationReconciliationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ReconcileAsyncReleasesExpiredUnlinkedFreeReservationAndCountersRemainConsistent()
    {
        await using var dbContext = CreateDbContext();
        var context = await SeedFreeTrialAsync(dbContext);
        var entitlement = CreateEntitlementService(dbContext);
        var reservationResult = await entitlement.ReserveScanAsync(CreateFreeRequest(context.UserId, context.ClientContext));
        Assert.True(reservationResult.Success);

        var reservation = await dbContext.ScanReservations.SingleAsync();
        reservation.ReservedAt = Now.AddHours(-2);
        reservation.ExpiresAt = Now.AddHours(-1);
        reservation.LastHeartbeatAt = Now.AddHours(-2);
        await dbContext.SaveChangesAsync();

        var service = CreateReconciliationService(dbContext);

        var result = await service.ReconcileAsync();

        Assert.Equal(1, result.CheckedReservations);
        Assert.Equal(1, result.ReleasedReservations);
        Assert.Equal(ScanReservationStatuses.Released, (await dbContext.ScanReservations.SingleAsync()).Status);
        Assert.Equal(0, (await dbContext.FreeTrialAccountUsages.SingleAsync()).ReservedScans);
        Assert.Equal(0, (await dbContext.FreeTrialDeviceUsages.SingleAsync()).ReservedScans);
        Assert.Equal(0, (await dbContext.FreeTrialIpUsages.SingleAsync()).ReservedScans);
    }

    [Fact]
    public async Task ReconcileAsyncDoesNotReleaseActiveRecentReservation()
    {
        await using var dbContext = CreateDbContext();
        var (reservationId, jobId) = await SeedPaidReservationWithJobAsync(dbContext, SubscriptionPlanCodes.Plus, JobStatus.Processing, Now);
        var service = CreateReconciliationService(dbContext);

        var result = await service.ReconcileAsync();

        Assert.Equal(1, result.CheckedReservations);
        Assert.Equal(1, result.SkippedActiveReservations);
        Assert.Equal(ScanReservationStatuses.Reserved, (await dbContext.ScanReservations.FindAsync(reservationId))!.Status);
        Assert.Equal(JobStatus.Processing, (await dbContext.AnalysisJobs.FindAsync(jobId))!.Status);
    }

    [Fact]
    public async Task ReconcileAsyncConsumesCompletedReservationAndIsIdempotent()
    {
        await using var dbContext = CreateDbContext();
        var (reservationId, _) = await SeedPaidReservationWithJobAsync(dbContext, SubscriptionPlanCodes.Pro, JobStatus.Completed, Now.AddMinutes(-1));
        var service = CreateReconciliationService(dbContext);

        var first = await service.ReconcileAsync();
        var second = await service.ReconcileAsync();

        Assert.Equal(1, first.ConsumedReservations);
        Assert.Equal(0, second.CheckedReservations);
        Assert.Equal(ScanReservationStatuses.Consumed, (await dbContext.ScanReservations.FindAsync(reservationId))!.Status);
        Assert.Equal(1, await dbContext.ScanUsages.CountAsync(usage => usage.ScanReservationId == reservationId));
    }

    [Fact]
    public async Task ReconcileAsyncReleasesFailedReservationAndIsIdempotent()
    {
        await using var dbContext = CreateDbContext();
        var (reservationId, _) = await SeedPaidReservationWithJobAsync(dbContext, SubscriptionPlanCodes.Plus, JobStatus.Failed, Now.AddMinutes(-1));
        var service = CreateReconciliationService(dbContext);

        var first = await service.ReconcileAsync();
        var second = await service.ReconcileAsync();

        Assert.Equal(1, first.ReleasedReservations);
        Assert.Equal(0, second.CheckedReservations);
        Assert.Equal(ScanReservationStatuses.Released, (await dbContext.ScanReservations.FindAsync(reservationId))!.Status);
        Assert.Empty(dbContext.ScanUsages);
    }

    [Fact]
    public async Task ReconcileAsyncFailsStaleActiveJobAndReleasesReservation()
    {
        await using var dbContext = CreateDbContext();
        var staleActivity = Now.AddHours(-2);
        var (reservationId, jobId) = await SeedPaidReservationWithJobAsync(dbContext, SubscriptionPlanCodes.Pro, JobStatus.Processing, staleActivity);
        var service = CreateReconciliationService(dbContext);

        var result = await service.ReconcileAsync();

        var job = await dbContext.AnalysisJobs.Include(candidate => candidate.Video).SingleAsync(candidate => candidate.Id == jobId);
        Assert.Equal(1, result.FailedStaleJobs);
        Assert.Equal(1, result.ReleasedReservations);
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(VideoStatus.Failed, job.Video.Status);
        Assert.Equal("STALE_ANALYSIS_JOB", job.ErrorCode);
        Assert.Equal(ScanReservationStatuses.Released, (await dbContext.ScanReservations.FindAsync(reservationId))!.Status);
    }

    [Theory]
    [InlineData(SubscriptionPlanCodes.Plus)]
    [InlineData(SubscriptionPlanCodes.Pro)]
    public async Task ReconcileAsyncReleasesPaidReservationWithoutLosingPlanQuota(string planCode)
    {
        await using var dbContext = CreateDbContext();
        await SeedPaidReservationWithJobAsync(dbContext, planCode, JobStatus.Failed, Now.AddMinutes(-1), scanLimit: 1);
        var service = CreateReconciliationService(dbContext);

        await service.ReconcileAsync();

        var entitlement = CreateEntitlementService(dbContext);
        var nextReservation = await entitlement.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = 1,
            AnalysisMode = planCode == SubscriptionPlanCodes.Pro ? AnalysisMode.Detailed : AnalysisMode.Basic,
            FileSizeBytes = 100_000_000
        });

        Assert.True(nextReservation.Success);
        Assert.Empty(dbContext.ScanUsages);
        Assert.Equal(1, await dbContext.ScanReservations.CountAsync(reservation => reservation.Status == ScanReservationStatuses.Reserved));
    }

    [Fact]
    public async Task ReconcileAsyncLeavesPausedReservationUntouched()
    {
        await using var dbContext = CreateDbContext();
        var oldActivity = Now.AddHours(-8);
        var (reservationId, jobId) = await SeedPaidReservationWithJobAsync(dbContext, SubscriptionPlanCodes.Pro, JobStatus.Paused, oldActivity);
        var service = CreateReconciliationService(dbContext);

        var result = await service.ReconcileAsync();

        Assert.Equal(1, result.SkippedActiveReservations);
        Assert.Equal(ScanReservationStatuses.Reserved, (await dbContext.ScanReservations.FindAsync(reservationId))!.Status);
        Assert.Equal(JobStatus.Paused, (await dbContext.AnalysisJobs.FindAsync(jobId))!.Status);
    }

    [Fact]
    public async Task ReconcileAsyncDoesNothingForAdminBecauseAdminCreatesNoReservation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, UserRole.Admin));
        await dbContext.SaveChangesAsync();
        var entitlement = CreateEntitlementService(dbContext);

        var reservation = await entitlement.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = 1,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = 100_000_000
        });
        var result = await CreateReconciliationService(dbContext).ReconcileAsync();

        Assert.True(reservation.Success);
        Assert.False(reservation.ReservationRequired);
        Assert.Equal(0, result.CheckedReservations);
        Assert.Empty(dbContext.ScanReservations);
    }

    [Fact]
    public void ReconcileAsyncUsesHangfireConcurrencyLock()
    {
        var method = typeof(ScanReservationReconciliationService).GetMethod(nameof(ScanReservationReconciliationService.ReconcileAsync), Type.EmptyTypes);

        Assert.NotNull(method);
        Assert.NotNull(method!.GetCustomAttributes(typeof(DisableConcurrentExecutionAttribute), inherit: true).SingleOrDefault());
    }

    private static async Task<(long UserId, SubscriptionClientContext ClientContext)> SeedFreeTrialAsync(AppDbContext dbContext)
    {
        var user = CreateUser(1);
        var device = CreateDevice();
        dbContext.Users.Add(user);
        dbContext.DeviceIdentities.Add(device);
        dbContext.SubscriptionPlans.Add(CreatePlan(SubscriptionPlanCodes.Free));
        await dbContext.SaveChangesAsync();

        return (user.Id, new SubscriptionClientContext
        {
            DeviceIdentityId = device.Id,
            IpHash = "ip-hash-1"
        });
    }

    private static async Task<(long ReservationId, long JobId)> SeedPaidReservationWithJobAsync(
        AppDbContext dbContext,
        string planCode,
        JobStatus jobStatus,
        DateTimeOffset lastActivity,
        int scanLimit = 10)
    {
        var user = CreateUser(1);
        var plan = CreatePlan(planCode, scanLimit);
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.Add(plan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = plan,
            Status = SubscriptionStatuses.Active,
            StartsAt = Now.AddDays(-1),
            ExpiresAt = Now.AddDays(30)
        });
        await dbContext.SaveChangesAsync();

        var entitlement = CreateEntitlementService(dbContext);
        var reservationResult = await entitlement.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = planCode == SubscriptionPlanCodes.Pro ? AnalysisMode.Detailed : AnalysisMode.Basic,
            FileSizeBytes = 100_000_000
        });
        Assert.True(reservationResult.Success);

        var video = new Video
        {
            UserId = user.Id,
            OriginalName = "sample.mp4",
            FileUrl = "videos/sample.mp4",
            ContentType = "video/mp4",
            FileExtension = ".mp4",
            FileSize = 100_000_000,
            Status = jobStatus == JobStatus.Completed ? VideoStatus.Completed : VideoStatus.Processing
        };
        var job = new AnalysisJob
        {
            Video = video,
            Status = jobStatus,
            Progress = jobStatus == JobStatus.Completed ? 100 : 50,
            CurrentStep = "Processing",
            ScanMode = planCode == SubscriptionPlanCodes.Pro ? AnalysisMode.Detailed.ToString() : AnalysisMode.Basic.ToString(),
            StartedAt = lastActivity,
            LastActivityAt = lastActivity,
            CompletedAt = jobStatus is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled ? lastActivity : null
        };
        dbContext.Videos.Add(video);
        dbContext.AnalysisJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var reservation = await dbContext.ScanReservations.SingleAsync();
        reservation.VideoId = video.Id;
        reservation.AnalysisJobId = job.Id;
        reservation.ExpiresAt = null;
        reservation.LastHeartbeatAt = lastActivity;
        await dbContext.SaveChangesAsync();

        return (reservation.Id, job.Id);
    }

    private static ScanReservationRequest CreateFreeRequest(long userId, SubscriptionClientContext context)
    {
        return new ScanReservationRequest
        {
            UserId = userId,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = 100_000_000,
            ClientContext = context
        };
    }

    private static EntitlementService CreateEntitlementService(AppDbContext dbContext)
    {
        return new EntitlementService(
            dbContext,
            Options.Create(new ScanReservationOptions
            {
                UnlinkedReservationTtlMinutes = 60,
                ActiveJobSafetyMarginMinutes = 5,
                ReconciliationBatchSize = 100
            }));
    }

    private static ScanReservationReconciliationService CreateReconciliationService(AppDbContext dbContext)
    {
        var options = Options.Create(new ScanReservationOptions
        {
            UnlinkedReservationTtlMinutes = 60,
            ActiveJobSafetyMarginMinutes = 5,
            ReconciliationBatchSize = 100
        });
        var processingOptions = Options.Create(new VideoProcessingOptions
        {
            WorkerTimeoutMinutes = 30,
            StaleActiveJobTimeoutMinutes = 30
        });

        return new ScanReservationReconciliationService(
            dbContext,
            CreateEntitlementService(dbContext),
            options,
            processingOptions,
            NullLogger<ScanReservationReconciliationService>.Instance);
    }

    private static User CreateUser(long id, UserRole role = UserRole.User)
    {
        return new User
        {
            Id = id,
            Name = $"User {id}",
            UserName = $"user{id}@example.com",
            Email = $"user{id}@example.com",
            PasswordHash = "hash",
            Role = role,
            IsActive = true,
            EmailConfirmed = true
        };
    }

    private static DeviceIdentity CreateDevice()
    {
        return new DeviceIdentity
        {
            DeviceTokenHash = Guid.NewGuid().ToString("N"),
            HashVersion = "hmac-sha256-v1",
            FirstSeenAt = Now,
            LastSeenAt = Now
        };
    }

    private static SubscriptionPlan CreatePlan(string code, int scanLimit = 10)
    {
        return code switch
        {
            SubscriptionPlanCodes.Free => new SubscriptionPlan
            {
                Id = 1,
                Code = SubscriptionPlanCodes.Free,
                Name = "Free",
                Currency = "PKR",
                PriceAmount = 0,
                ScanLimit = 2,
                MaxVideoSizeBytes = VideoUploadSizeLimits.FreeMaxVideoSizeBytes,
                AllowsSmartScan = true,
                AllowsDetailedScan = false,
                IsActive = true,
                SortOrder = 1
            },
            SubscriptionPlanCodes.Pro => new SubscriptionPlan
            {
                Id = 3,
                Code = SubscriptionPlanCodes.Pro,
                Name = "Pro",
                Currency = "PKR",
                PriceAmount = 999,
                ScanLimit = scanLimit,
                MaxVideoSizeBytes = VideoUploadSizeLimits.ProMaxVideoSizeBytes,
                AllowsSmartScan = true,
                AllowsDetailedScan = true,
                ValidityDays = 30,
                IsActive = true,
                SortOrder = 3
            },
            _ => new SubscriptionPlan
            {
                Id = 2,
                Code = SubscriptionPlanCodes.Plus,
                Name = "Plus",
                Currency = "PKR",
                PriceAmount = 499,
                ScanLimit = scanLimit,
                MaxVideoSizeBytes = VideoUploadSizeLimits.PlusMaxVideoSizeBytes,
                AllowsSmartScan = true,
                AllowsDetailedScan = false,
                ValidityDays = 30,
                IsActive = true,
                SortOrder = 2
            }
        };
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }
}
