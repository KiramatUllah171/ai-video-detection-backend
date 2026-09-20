using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AiVideoDetection.Tests.Subscriptions;

public class EntitlementServiceTests
{
    [Fact]
    public async Task ReserveScanAsyncAllowsAdminWithoutPersistingQuotaReservation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, UserRole.Admin));
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = 1,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes
        });

        Assert.True(result.Success);
        Assert.False(result.ReservationRequired);
        Assert.Null(result.ReservationId);
        Assert.Equal(ScanReservationKinds.InternalUnlimited, result.EntitlementType);
        Assert.Empty(dbContext.ScanReservations);
    }

    [Fact]
    public async Task GetStatusAsyncReportsAdminAbsoluteVideoLimit()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, UserRole.Admin));
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var response = await service.GetStatusAsync(1);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.True(response.Data.IsAdmin);
        Assert.Equal(ScanReservationKinds.InternalUnlimited, response.Data.PlanCode);
        Assert.Equal(VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes, response.Data.MaxVideoSizeBytes);
    }

    [Fact]
    public async Task GetStatusAsyncCreatesFreePlanWhenSeedDataIsMissing()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1));
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var response = await service.GetStatusAsync(1);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(SubscriptionPlanCodes.Free, response.Data.PlanCode);
        Assert.Equal(1, await dbContext.SubscriptionPlans.CountAsync(plan => plan.Code == SubscriptionPlanCodes.Free));
    }

    [Fact]
    public async Task ReserveScanAsyncExhaustsFreeTrialAcrossAccountDeviceAndIp()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var device = CreateDevice();
        dbContext.Users.Add(user);
        dbContext.DeviceIdentities.Add(device);
        dbContext.SubscriptionPlans.Add(CreateFreePlan());
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);
        var context = new SubscriptionClientContext
        {
            DeviceIdentityId = device.Id,
            IpHash = "ip-hash-1"
        };

        var first = await service.ReserveScanAsync(CreateRequest(user.Id, context));
        var second = await service.ReserveScanAsync(CreateRequest(user.Id, context));
        var third = await service.ReserveScanAsync(CreateRequest(user.Id, context));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.False(third.Success);
        Assert.Equal(SubscriptionErrorCodes.FreeTrialExhausted, third.ErrorCode);
        Assert.Equal(2, await dbContext.ScanReservations.CountAsync());
        Assert.Equal(2, (await dbContext.FreeTrialAccountUsages.SingleAsync()).ReservedScans);
        Assert.Equal(2, (await dbContext.FreeTrialDeviceUsages.SingleAsync()).ReservedScans);
        Assert.Equal(2, (await dbContext.FreeTrialIpUsages.SingleAsync()).ReservedScans);
    }

    [Fact]
    public async Task ReserveScanAsyncAllowsOnlyOneGuestUploadForSharedDeviceAndIp()
    {
        await using var dbContext = CreateDbContext();
        var sharedDevice = CreateDevice();
        dbContext.Users.AddRange(CreateUser(1), CreateUser(2));
        dbContext.DeviceIdentities.Add(sharedDevice);
        dbContext.SubscriptionPlans.Add(CreateFreePlan());
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);
        var context = new SubscriptionClientContext
        {
            DeviceIdentityId = sharedDevice.Id,
            IpHash = "guest-ip-hash"
        };

        var first = await service.ReserveScanAsync(CreateRequest(1, context, isGuestUpload: true));
        var second = await service.ReserveScanAsync(CreateRequest(2, context, isGuestUpload: true));
        var authenticatedSecond = await service.ReserveScanAsync(CreateRequest(2, context));

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(SubscriptionErrorCodes.GuestLimitReached, second.ErrorCode);
        Assert.True(authenticatedSecond.Success);
        Assert.Equal(2, await dbContext.ScanReservations.CountAsync());
    }

    [Fact]
    public async Task GetGuestUploadStatusAsyncReportsLimitReachedAfterGuestUsage()
    {
        await using var dbContext = CreateDbContext();
        var sharedDevice = CreateDevice();
        dbContext.Users.Add(CreateUser(1));
        dbContext.DeviceIdentities.Add(sharedDevice);
        dbContext.SubscriptionPlans.Add(CreateFreePlan());
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);
        var context = new SubscriptionClientContext
        {
            DeviceIdentityId = sharedDevice.Id,
            IpHash = "guest-ip-hash"
        };

        var initial = await service.GetGuestUploadStatusAsync(context);
        var reservation = await service.ReserveScanAsync(CreateRequest(1, context, isGuestUpload: true));
        var used = await service.GetGuestUploadStatusAsync(context);

        Assert.True(initial.Success);
        Assert.True(initial.Data!.CanUpload);
        Assert.Equal(1, initial.Data.RemainingUploads);
        Assert.True(reservation.Success);
        Assert.True(used.Success);
        Assert.False(used.Data!.CanUpload);
        Assert.Equal(0, used.Data.RemainingUploads);
        Assert.Equal(SubscriptionErrorCodes.GuestLimitReached, used.Data.BlockReasonCode);
    }

    [Fact]
    public async Task GetGuestUploadStatusAsyncAllowsFreshGuestWhenFreePlanWasRemoved()
    {
        await using var dbContext = CreateDbContext();
        var sharedDevice = CreateDevice();
        dbContext.DeviceIdentities.Add(sharedDevice);
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var response = await service.GetGuestUploadStatusAsync(new SubscriptionClientContext
        {
            DeviceIdentityId = sharedDevice.Id,
            IpHash = "fresh-guest-ip"
        });

        Assert.True(response.Success);
        Assert.True(response.Data!.CanUpload);
        Assert.Equal(1, response.Data.RemainingUploads);
        Assert.Null(response.Data.BlockReasonCode);
        Assert.Equal(SubscriptionPlanCodes.Free, (await dbContext.SubscriptionPlans.SingleAsync()).Code);
    }

    [Fact]
    public async Task ReserveScanAsyncAllowsFirstGuestUploadWhenFreePlanWasRemoved()
    {
        await using var dbContext = CreateDbContext();
        var sharedDevice = CreateDevice();
        dbContext.Users.Add(CreateUser(1));
        dbContext.DeviceIdentities.Add(sharedDevice);
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);
        var context = new SubscriptionClientContext
        {
            DeviceIdentityId = sharedDevice.Id,
            IpHash = "fresh-guest-ip"
        };

        var result = await service.ReserveScanAsync(CreateRequest(1, context, isGuestUpload: true));

        Assert.True(result.Success);
        Assert.Equal(ScanReservationKinds.FreeTrial, result.EntitlementType);
        Assert.Equal(1, await dbContext.ScanReservations.CountAsync());
        Assert.Equal(SubscriptionPlanCodes.Free, (await dbContext.SubscriptionPlans.SingleAsync()).Code);
    }

    [Fact]
    public async Task ReserveScanAsyncExhaustsFreeTrialForMultiAccountReuseOfSameDevice()
    {
        await using var dbContext = CreateDbContext();
        var sharedDevice = CreateDevice();
        dbContext.Users.AddRange(CreateUser(1), CreateUser(2));
        dbContext.DeviceIdentities.Add(sharedDevice);
        dbContext.SubscriptionPlans.Add(CreateFreePlan());
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var first = await service.ReserveScanAsync(CreateRequest(1, new SubscriptionClientContext
        {
            DeviceIdentityId = sharedDevice.Id,
            IpHash = "ip-hash-1"
        }));
        var second = await service.ReserveScanAsync(CreateRequest(1, new SubscriptionClientContext
        {
            DeviceIdentityId = sharedDevice.Id,
            IpHash = "ip-hash-2"
        }));
        var third = await service.ReserveScanAsync(CreateRequest(2, new SubscriptionClientContext
        {
            DeviceIdentityId = sharedDevice.Id,
            IpHash = "ip-hash-3"
        }));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.False(third.Success);
        Assert.Equal(SubscriptionErrorCodes.FreeTrialExhausted, third.ErrorCode);
        Assert.Equal(2, (await dbContext.FreeTrialDeviceUsages.SingleAsync()).ReservedScans);
        Assert.Equal(0, (await dbContext.FreeTrialDeviceUsages.SingleAsync()).ConsumedScans);
    }

    [Fact]
    public async Task ReserveScanAsyncLimitsDifferentDevicesSharingSameIp()
    {
        await using var dbContext = CreateDbContext();
        var devices = new[] { CreateDevice(), CreateDevice(), CreateDevice() };
        dbContext.Users.AddRange(CreateUser(1), CreateUser(2), CreateUser(3));
        dbContext.DeviceIdentities.AddRange(devices);
        dbContext.SubscriptionPlans.Add(CreateFreePlan());
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var first = await service.ReserveScanAsync(CreateRequest(1, new SubscriptionClientContext
        {
            DeviceIdentityId = devices[0].Id,
            IpHash = "shared-ip-hash"
        }));
        var second = await service.ReserveScanAsync(CreateRequest(2, new SubscriptionClientContext
        {
            DeviceIdentityId = devices[1].Id,
            IpHash = "shared-ip-hash"
        }));
        var third = await service.ReserveScanAsync(CreateRequest(3, new SubscriptionClientContext
        {
            DeviceIdentityId = devices[2].Id,
            IpHash = "shared-ip-hash"
        }));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.False(third.Success);
        Assert.Equal(SubscriptionErrorCodes.FreeTrialExhausted, third.ErrorCode);
        Assert.Equal(2, (await dbContext.FreeTrialIpUsages.SingleAsync()).ReservedScans);
    }

    [Fact]
    public async Task GetStatusAsyncReportsPartialFreeTrialUsageAcrossAccountDeviceAndIp()
    {
        await using var dbContext = CreateDbContext();
        var (service, userId, context) = await SeedFreeTrialAsync(dbContext);
        var reservation = await service.ReserveScanAsync(CreateRequest(userId, context));
        await service.ConsumeReservationAsync(reservation.ReservationId!.Value, videoId: 10, analysisJobId: 20);

        var status = await service.GetStatusAsync(userId, context);

        Assert.True(status.Success);
        Assert.Equal(1, status.Data!.UsedScans);
        Assert.Equal(0, status.Data.ReservedScans);
        Assert.Equal(1, status.Data.RemainingScans);
        Assert.Equal(1, status.Data.FreeTrial!.AccountRemainingScans);
        Assert.Equal(1, status.Data.FreeTrial.DeviceRemainingScans);
        Assert.Equal(1, status.Data.FreeTrial.IpRemainingScans);
    }

    [Fact]
    public async Task ReserveScanAsyncRejectsDetailedScanForFreeTrial()
    {
        await using var dbContext = CreateDbContext();
        var (service, userId, context) = await SeedFreeTrialAsync(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = userId,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = 100_000_000,
            ClientContext = context
        });

        Assert.False(result.Success);
        Assert.Equal(SubscriptionErrorCodes.DetailedScanNotAllowed, result.ErrorCode);
        Assert.Empty(dbContext.ScanReservations);
    }

    [Fact]
    public async Task ReserveScanAsyncLimitsConcurrentFreeReservationsBySharedDeviceAndIp()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var seedContext = CreateDbContext(databaseName, databaseRoot))
        {
            var sharedDevice = CreateDevice();
            sharedDevice.Id = 1;
            seedContext.Users.AddRange(CreateUser(1), CreateUser(2), CreateUser(3));
            seedContext.DeviceIdentities.Add(sharedDevice);
            seedContext.SubscriptionPlans.Add(CreateFreePlan());
            await seedContext.SaveChangesAsync();
        }

        var reservations = await Task.WhenAll(Enumerable.Range(1, 3).Select(async userId =>
        {
            await using var dbContext = CreateDbContext(databaseName, databaseRoot);
            var service = new EntitlementService(dbContext);
            return await service.ReserveScanAsync(CreateRequest(userId, new SubscriptionClientContext
            {
                DeviceIdentityId = 1,
                IpHash = "shared-concurrent-ip"
            }));
        }));

        await using var verifyContext = CreateDbContext(databaseName, databaseRoot);
        Assert.Equal(2, reservations.Count(result => result.Success));
        Assert.Single(reservations, result => !result.Success && result.ErrorCode == SubscriptionErrorCodes.FreeTrialExhausted);
        Assert.Equal(2, await verifyContext.ScanReservations.CountAsync());
        Assert.Equal(2, (await verifyContext.FreeTrialDeviceUsages.SingleAsync()).ReservedScans);
        Assert.Equal(2, (await verifyContext.FreeTrialIpUsages.SingleAsync()).ReservedScans);
    }

    [Fact]
    public async Task ReleaseReservationAsyncReturnsFreeTrialCounters()
    {
        await using var dbContext = CreateDbContext();
        var (service, userId, context) = await SeedFreeTrialAsync(dbContext);
        var reservation = await service.ReserveScanAsync(CreateRequest(userId, context));

        var release = await service.ReleaseReservationAsync(reservation.ReservationId!.Value, "Upload failed.");
        var nextReservation = await service.ReserveScanAsync(CreateRequest(userId, context));

        Assert.True(release.Success);
        Assert.True(nextReservation.Success);
        var releasedReservation = await dbContext.ScanReservations.FindAsync(reservation.ReservationId.Value);
        Assert.Equal(ScanReservationStatuses.Released, releasedReservation!.Status);
        Assert.Equal(1, (await dbContext.FreeTrialAccountUsages.SingleAsync()).ReservedScans);
        Assert.Equal(0, (await dbContext.FreeTrialAccountUsages.SingleAsync()).ConsumedScans);
    }

    [Fact]
    public async Task ConsumeReservationAsyncMovesFreeTrialCountersAndCreatesUsage()
    {
        await using var dbContext = CreateDbContext();
        var (service, userId, context) = await SeedFreeTrialAsync(dbContext);
        var reservation = await service.ReserveScanAsync(CreateRequest(userId, context));

        var consumed = await service.ConsumeReservationAsync(reservation.ReservationId!.Value, videoId: 10, analysisJobId: 20);

        Assert.True(consumed.Success);
        var accountUsage = await dbContext.FreeTrialAccountUsages.SingleAsync();
        Assert.Equal(0, accountUsage.ReservedScans);
        Assert.Equal(1, accountUsage.ConsumedScans);
        var scanUsage = await dbContext.ScanUsages.SingleAsync();
        Assert.Equal(ScanReservationKinds.FreeTrial, scanUsage.EntitlementType);
        Assert.Equal(10, scanUsage.VideoId);
        Assert.Equal(20, scanUsage.AnalysisJobId);
    }

    [Fact]
    public async Task ReserveScanAsyncUsesPaidSubscriptionBeforeFreeTrial()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var plusPlan = CreatePlusPlan(scanLimit: 1);
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.AddRange(CreateFreePlan(), plusPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = plusPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var first = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = 100_000_000
        });
        var second = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = 100_000_000
        });

        Assert.True(first.Success);
        Assert.Equal(ScanReservationKinds.PaidSubscription, first.EntitlementType);
        Assert.False(second.Success);
        Assert.Equal(SubscriptionErrorCodes.ScanQuotaExhausted, second.ErrorCode);
        Assert.Empty(dbContext.FreeTrialAccountUsages);
    }

    [Fact]
    public async Task ReserveScanAsyncLimitsConcurrentPlusReservationsToPlanQuota()
    {
        var databaseName = Guid.NewGuid().ToString();
        var databaseRoot = new InMemoryDatabaseRoot();
        await using (var seedContext = CreateDbContext(databaseName, databaseRoot))
        {
            var user = CreateUser(1);
            var plusPlan = CreatePlusPlan(scanLimit: 1);
            seedContext.Users.Add(user);
            seedContext.SubscriptionPlans.Add(plusPlan);
            seedContext.UserSubscriptions.Add(new UserSubscription
            {
                User = user,
                SubscriptionPlan = plusPlan,
                Status = SubscriptionStatuses.Active,
                StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
            });
            await seedContext.SaveChangesAsync();
        }

        var reservations = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var dbContext = CreateDbContext(databaseName, databaseRoot);
            var service = new EntitlementService(dbContext);
            return await service.ReserveScanAsync(new ScanReservationRequest
            {
                UserId = 1,
                AnalysisMode = AnalysisMode.Basic,
                FileSizeBytes = 100_000_000
            });
        }));

        await using var verifyContext = CreateDbContext(databaseName, databaseRoot);
        Assert.Single(reservations, result => result.Success);
        Assert.Single(reservations, result => !result.Success && result.ErrorCode == SubscriptionErrorCodes.ScanQuotaExhausted);
        Assert.Equal(1, await verifyContext.ScanReservations.CountAsync());
    }

    [Fact]
    public async Task ReserveScanAsyncAllowsPlusSmartScanAboveFreeLimit()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var plusPlan = CreatePlusPlan(scanLimit: 10);
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.AddRange(CreateFreePlan(), plusPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = plusPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = VideoUploadSizeLimits.PlusMaxVideoSizeBytes
        });

        Assert.True(result.Success);
        Assert.Equal(ScanReservationKinds.PaidSubscription, result.EntitlementType);
    }

    [Fact]
    public async Task ReserveScanAsyncRejectsFreeSmartScanAboveFreeLimit()
    {
        await using var dbContext = CreateDbContext();
        var (service, userId, context) = await SeedFreeTrialAsync(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = userId,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = VideoUploadSizeLimits.FreeMaxVideoSizeBytes + 1,
            ClientContext = context
        });

        Assert.False(result.Success);
        Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, result.ErrorCode);
        Assert.Contains("Free plan supports videos up to 200 MB", result.Message);
    }

    [Theory]
    [InlineData(199, true)]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public async Task ReserveScanAsyncEnforcesFreeOriginalSizeBoundary(int megabytes, bool expectedSuccess)
    {
        await using var dbContext = CreateDbContext();
        var (service, userId, context) = await SeedFreeTrialAsync(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = userId,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = ToBytes(megabytes),
            ClientContext = context
        });

        Assert.Equal(expectedSuccess, result.Success);
        if (!expectedSuccess)
        {
            Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, result.ErrorCode);
        }
    }

    [Theory]
    [InlineData(VideoUploadSizeLimits.FreeMaxVideoSizeBytes, true)]
    [InlineData(VideoUploadSizeLimits.FreeMaxVideoSizeBytes + 1, false)]
    public async Task ReserveScanAsyncEnforcesFreeExactByteBoundary(long fileSizeBytes, bool expectedSuccess)
    {
        await using var dbContext = CreateDbContext();
        var (service, userId, context) = await SeedFreeTrialAsync(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = userId,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = fileSizeBytes,
            ClientContext = context
        });

        Assert.Equal(expectedSuccess, result.Success);
        if (!expectedSuccess)
        {
            Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, result.ErrorCode);
        }
    }

    [Theory]
    [InlineData(249, true)]
    [InlineData(250, true)]
    [InlineData(251, false)]
    public async Task ReserveScanAsyncEnforcesPlusOriginalSizeBoundary(int megabytes, bool expectedSuccess)
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var plusPlan = CreatePlusPlan(scanLimit: 10);
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.AddRange(CreateFreePlan(), plusPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = plusPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = ToBytes(megabytes)
        });

        Assert.Equal(expectedSuccess, result.Success);
        if (!expectedSuccess)
        {
            Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, result.ErrorCode);
            Assert.Contains("Plus plan supports videos up to 250 MB", result.Message);
        }
    }

    [Theory]
    [InlineData(VideoUploadSizeLimits.PlusMaxVideoSizeBytes, true)]
    [InlineData(VideoUploadSizeLimits.PlusMaxVideoSizeBytes + 1, false)]
    public async Task ReserveScanAsyncEnforcesPlusExactByteBoundary(long fileSizeBytes, bool expectedSuccess)
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var plusPlan = CreatePlusPlan(scanLimit: 10);
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.AddRange(CreateFreePlan(), plusPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = plusPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = fileSizeBytes
        });

        Assert.Equal(expectedSuccess, result.Success);
        if (!expectedSuccess)
        {
            Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, result.ErrorCode);
            Assert.Contains("Plus plan supports videos up to 250 MB", result.Message);
        }
    }

    [Theory]
    [InlineData(299, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public async Task ReserveScanAsyncEnforcesProOriginalSizeBoundary(int megabytes, bool expectedSuccess)
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var proPlan = CreateProPlan();
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.Add(proPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = proPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = ToBytes(megabytes)
        });

        Assert.Equal(expectedSuccess, result.Success);
        if (!expectedSuccess)
        {
            Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, result.ErrorCode);
            Assert.Contains("Pro plan supports videos up to 300 MB", result.Message);
        }
    }

    [Theory]
    [InlineData(VideoUploadSizeLimits.ProMaxVideoSizeBytes, true)]
    [InlineData(VideoUploadSizeLimits.ProMaxVideoSizeBytes + 1, false)]
    public async Task ReserveScanAsyncEnforcesProExactByteBoundary(long fileSizeBytes, bool expectedSuccess)
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var proPlan = CreateProPlan();
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.Add(proPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = proPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = fileSizeBytes
        });

        Assert.Equal(expectedSuccess, result.Success);
        if (!expectedSuccess)
        {
            Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, result.ErrorCode);
            Assert.Contains("Pro plan supports videos up to 300 MB", result.Message);
        }
    }

    [Fact]
    public async Task ReserveScanAsyncAllowsAdminAtExact300MiBCommercialBypass()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, UserRole.Admin));
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = 1,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes
        });

        Assert.True(result.Success);
        Assert.Equal(ScanReservationKinds.InternalUnlimited, result.EntitlementType);
        Assert.False(result.ReservationRequired);
    }

    [Fact]
    public async Task ReserveScanAsyncRejectsDetailedScanForPlusPlan()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var plusPlan = CreatePlusPlan(scanLimit: 10);
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.Add(plusPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = plusPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var result = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = 100_000_000
        });

        Assert.False(result.Success);
        Assert.Equal(SubscriptionErrorCodes.DetailedScanNotAllowed, result.ErrorCode);
    }

    [Fact]
    public async Task ReserveScanAsyncEnforcesProQuotaAfterAllowingDetailedScan()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var proPlan = CreateProPlan();
        proPlan.ScanLimit = 1;
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.Add(proPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = proPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var first = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = VideoUploadSizeLimits.ProMaxVideoSizeBytes
        });
        var second = await service.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = user.Id,
            AnalysisMode = AnalysisMode.Detailed,
            FileSizeBytes = 100_000_000
        });

        Assert.True(first.Success);
        Assert.Equal(ScanReservationKinds.PaidSubscription, first.EntitlementType);
        Assert.False(second.Success);
        Assert.Equal(SubscriptionErrorCodes.ScanQuotaExhausted, second.ErrorCode);
    }

    [Fact]
    public async Task GetStatusAsyncTreatsExpiredSubscriptionAsFreeTrial()
    {
        await using var dbContext = CreateDbContext();
        var user = CreateUser(1);
        var proPlan = CreateProPlan();
        dbContext.Users.Add(user);
        dbContext.SubscriptionPlans.AddRange(CreateFreePlan(), proPlan);
        dbContext.UserSubscriptions.Add(new UserSubscription
        {
            User = user,
            SubscriptionPlan = proPlan,
            Status = SubscriptionStatuses.Active,
            StartsAt = DateTimeOffset.UtcNow.AddDays(-40),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await dbContext.SaveChangesAsync();
        var service = new EntitlementService(dbContext);

        var response = await service.GetStatusAsync(user.Id);

        Assert.True(response.Success);
        Assert.Equal(SubscriptionPlanCodes.Free, response.Data!.PlanCode);
        Assert.False(response.Data.IsPaid);
        Assert.Equal(SubscriptionStatuses.Expired, (await dbContext.UserSubscriptions.SingleAsync()).Status);
    }

    private static async Task<(EntitlementService Service, long UserId, SubscriptionClientContext Context)> SeedFreeTrialAsync(
        AppDbContext dbContext)
    {
        var user = CreateUser(1);
        var device = CreateDevice();
        dbContext.Users.Add(user);
        dbContext.DeviceIdentities.Add(device);
        dbContext.SubscriptionPlans.Add(CreateFreePlan());
        await dbContext.SaveChangesAsync();

        return (new EntitlementService(dbContext), user.Id, new SubscriptionClientContext
        {
            DeviceIdentityId = device.Id,
            IpHash = "ip-hash-1"
        });
    }

    private static ScanReservationRequest CreateRequest(
        long userId,
        SubscriptionClientContext context,
        bool isGuestUpload = false)
    {
        return new ScanReservationRequest
        {
            UserId = userId,
            AnalysisMode = AnalysisMode.Basic,
            FileSizeBytes = 100_000_000,
            ClientContext = context,
            IsGuestUpload = isGuestUpload
        };
    }

    private static long ToBytes(int megabytes)
    {
        return megabytes * 1_048_576L;
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
            FirstSeenAt = DateTimeOffset.UtcNow,
            LastSeenAt = DateTimeOffset.UtcNow
        };
    }

    private static SubscriptionPlan CreateFreePlan()
    {
        return new SubscriptionPlan
        {
            Id = 1,
            Code = SubscriptionPlanCodes.Free,
            Name = "Free Trial",
            Currency = "PKR",
            PriceAmount = 0,
            ScanLimit = 2,
            MaxVideoSizeBytes = VideoUploadSizeLimits.FreeMaxVideoSizeBytes,
            AllowsSmartScan = true,
            AllowsDetailedScan = false,
            IsActive = true,
            SortOrder = 0
        };
    }

    private static SubscriptionPlan CreatePlusPlan(int scanLimit)
    {
        return new SubscriptionPlan
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
            SortOrder = 1
        };
    }

    private static SubscriptionPlan CreateProPlan()
    {
        return new SubscriptionPlan
        {
            Id = 3,
            Code = SubscriptionPlanCodes.Pro,
            Name = "Pro",
            Currency = "PKR",
            PriceAmount = 999,
            ScanLimit = 25,
            MaxVideoSizeBytes = VideoUploadSizeLimits.ProMaxVideoSizeBytes,
            AllowsSmartScan = true,
            AllowsDetailedScan = true,
            ValidityDays = 30,
            IsActive = true,
            SortOrder = 2
        };
    }

    private static AppDbContext CreateDbContext(string? databaseName = null, InMemoryDatabaseRoot? databaseRoot = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        if (databaseRoot is null)
        {
            optionsBuilder.UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString());
        }
        else
        {
            optionsBuilder.UseInMemoryDatabase(databaseName ?? Guid.NewGuid().ToString(), databaseRoot);
        }

        return new AppDbContext(optionsBuilder.Options);
    }
}
