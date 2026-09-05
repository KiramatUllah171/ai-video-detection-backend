using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.DTOs;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Videos;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using AiVideoDetection.Application.Videos.Validators;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos;
using AiVideoDetection.Infrastructure.Videos.Processing;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class VideoServiceTests
{
    [Fact]
    public async Task UploadValidVideoCreatesVideoAndAnalysisJob()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, storage, queue);

        var response = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(1, await dbContext.Videos.CountAsync());
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, queue.EnqueueCalls);
        Assert.Equal(JobStatus.Queued, (await dbContext.AnalysisJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task UploadLinksSuccessfulReservationToCreatedVideoAndJob()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.ScanReservations.Add(new ScanReservation
        {
            Id = 50,
            UserId = 1,
            SubscriptionPlanId = 1,
            ReservationKind = ScanReservationKinds.FreeTrial,
            Status = ScanReservationStatuses.Reserved,
            AnalysisMode = AnalysisMode.Basic.ToString(),
            FileSizeBytes = 100,
            ReservedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
        var entitlement = new FakeEntitlementService { NextReservationId = 50 };
        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            new FakeAnalysisJobQueue(),
            entitlement);

        var response = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.True(response.Success);
        var reservation = await dbContext.ScanReservations.SingleAsync();
        Assert.Equal(response.Data!.VideoId, reservation.VideoId);
        Assert.Equal(response.Data.JobId, reservation.AnalysisJobId);
    }

    [Fact]
    public async Task UploadRejectsEntitlementFailureBeforeStorageAndQueue()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var entitlement = new FakeEntitlementService
        {
            NextResult = ScanReservationResult.Failed(
                "Your free trial has already been used. Choose a plan to continue analyzing videos.",
                SubscriptionErrorCodes.FreeTrialExhausted)
        };
        var service = CreateService(dbContext, storage, queue, entitlement);

        var response = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.False(response.Success);
        Assert.Equal(SubscriptionErrorCodes.FreeTrialExhausted, response.ErrorCode);
        Assert.Equal(0, storage.UploadCalls);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(0, await dbContext.Videos.CountAsync());
    }

    [Fact]
    public async Task UploadRejectsWhenServerStorageCapacityIsLowBeforeQuotaReservation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var entitlement = new FakeEntitlementService();
        var service = CreateService(
            dbContext,
            storage,
            queue,
            entitlement,
            storageCapacityService: new FakeVideoStorageCapacityService
            {
                UploadResult = VideoStorageCapacityCheckResult.Failed(
                    VideoInfrastructureErrorCodes.ServerStorageCapacityLow,
                    "The server is temporarily unable to accept this video. Please try again later.")
            });

        var response = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.False(response.Success);
        Assert.Equal(VideoInfrastructureErrorCodes.ServerStorageCapacityLow, response.ErrorCode);
        Assert.Equal(0, entitlement.ReserveCalls);
        Assert.Equal(0, storage.UploadCalls);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(0, await dbContext.Videos.CountAsync());
    }

    [Fact]
    public async Task UploadRejectsWhenConcurrentUploadGateIsFullBeforeQuotaReservation()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var entitlement = new FakeEntitlementService();
        var service = CreateService(
            dbContext,
            storage,
            queue,
            entitlement,
            workloadGate: new RejectingVideoWorkloadGate());

        var response = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.False(response.Success);
        Assert.Equal(VideoInfrastructureErrorCodes.UploadConcurrencyLimitReached, response.ErrorCode);
        Assert.Equal(0, entitlement.ReserveCalls);
        Assert.Equal(0, storage.UploadCalls);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(0, await dbContext.Videos.CountAsync());
    }

    [Fact]
    public async Task UploadDetailedVideoStoresDetailedScanMode()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.UploadAsync(CreateUploadRequest(analysisMode: AnalysisMode.Detailed), 1, null);

        Assert.True(response.Success);
        var job = await dbContext.AnalysisJobs.SingleAsync();
        Assert.Equal("Detailed", job.ScanMode);
    }

    [Fact]
    public async Task UploadRejectsAdminFileAboveAbsoluteTechnicalLimit()
    {
        await using var dbContext = CreateDbContext();
        var admin = CreateUser(1, "admin@example.com");
        admin.Role = UserRole.Admin;
        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, storage, queue);

        var response = await service.UploadAsync(
            CreateUploadRequest(declaredLength: VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes + 1),
            1,
            null);

        Assert.False(response.Success);
        Assert.Contains("Videos larger than 300 MB are not supported at this time.", response.Errors);
        Assert.Equal(0, storage.UploadCalls);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task UploadAllowsAdminFileAtExactAbsoluteTechnicalLimit()
    {
        await using var dbContext = CreateDbContext();
        var admin = CreateUser(1, "admin@example.com");
        admin.Role = UserRole.Admin;
        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, storage, queue);

        var response = await service.UploadAsync(
            CreateUploadRequest(declaredLength: VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes),
            1,
            null);

        Assert.True(response.Success);
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, queue.EnqueueCalls);
        Assert.Equal(VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes, (await dbContext.Videos.SingleAsync()).FileSize);
    }


    [Fact]
    public async Task UploadDuplicateVideoForSameUserReusesExistingRecord()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, storage, queue);

        var first = await service.UploadAsync(CreateUploadRequest(), 1, null);
        var second = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotNull(first.Data);
        Assert.NotNull(second.Data);
        Assert.Equal(first.Data.VideoId, second.Data.VideoId);
        Assert.Equal(1, await dbContext.Videos.CountAsync());
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, queue.EnqueueCalls);
    }

    [Fact]
    public async Task UploadRejectsUnsupportedFileSignature()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, storage, queue);

        var response = await service.UploadAsync(CreateUploadRequest([1, 2, 3, 4, 5, 6, 7, 8]), 1, null);

        Assert.False(response.Success);
        Assert.Equal("The uploaded file content does not match a supported video format.", response.Message);
        Assert.Equal(0, await dbContext.Videos.CountAsync());
        Assert.Equal(0, await dbContext.AnalysisJobs.CountAsync());
        Assert.Equal(0, storage.UploadCalls);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Theory]
    [InlineData("sample.mp4", "video/mp4", "mp4")]
    [InlineData("sample.mov", "video/quicktime", "mov")]
    [InlineData("legacy.mov", "video/quicktime", "mov-legacy")]
    [InlineData("sample.avi", "video/x-msvideo", "avi")]
    [InlineData("sample.mkv", "video/x-matroska", "ebml")]
    [InlineData("sample.webm", "video/webm", "ebml")]
    public async Task UploadAcceptsSupportedVideoContainerSignatures(string fileName, string contentType, string signatureKind)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, storage, queue);

        var response = await service.UploadAsync(
            CreateUploadRequest(CreateVideoHeader(signatureKind), fileName: fileName, contentType: contentType),
            1,
            null);

        Assert.True(response.Success);
        Assert.Equal(1, await dbContext.Videos.CountAsync());
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, queue.EnqueueCalls);
    }

    [Fact]
    public async Task UploadAcceptsSupportedContainerWhenFfprobeFallbackFindsVideoStream()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(
            dbContext,
            storage,
            queue,
            metadataExtractionService: new FakeMetadataExtractionService { HasVideoStream = true });

        var response = await service.UploadAsync(
            CreateUploadRequest([1, 2, 3, 4, 5, 6, 7, 8], fileName: "sample.avi", contentType: "video/avi"),
            1,
            null);

        Assert.True(response.Success);
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, queue.EnqueueCalls);
    }

    [Fact]
    public async Task UploadDoesNotReserveQuotaForRejectedSignature()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var entitlement = new FakeEntitlementService();
        var service = CreateService(
            dbContext,
            new FakeObjectStorageService(),
            new FakeAnalysisJobQueue(),
            entitlement);

        var response = await service.UploadAsync(CreateUploadRequest([1, 2, 3, 4, 5, 6, 7, 8]), 1, null);

        Assert.False(response.Success);
        Assert.Equal(0, entitlement.ReserveCalls);
    }

    [Fact]
    public async Task UploadAttemptsStorageCleanupWhenDatabaseSaveFails()
    {
        await using var dbContext = new FailingSaveAppDbContext(CreateOptions());
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage, new FakeAnalysisJobQueue());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(CreateUploadRequest(), 1, null));

        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, storage.DeleteCalls);
    }

    [Fact]
    public async Task UploadQueueFailureReleasesReservationAndCleansStoredObject()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var entitlement = new FakeEntitlementService { NextReservationId = 50 };
        var service = CreateService(dbContext, storage, new FailingAnalysisJobQueue(), entitlement);

        var response = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.False(response.Success);
        Assert.Equal(VideoInfrastructureErrorCodes.AnalysisQueueUnavailable, response.ErrorCode);
        Assert.Equal(1, entitlement.ReserveCalls);
        Assert.Equal(1, entitlement.ReleaseCalls);
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, storage.DeleteCalls);
        var job = await dbContext.AnalysisJobs.SingleAsync();
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(VideoInfrastructureErrorCodes.AnalysisQueueUnavailable, job.ErrorCode);
        Assert.Equal(string.Empty, (await dbContext.Videos.SingleAsync()).FileUrl);
    }

    [Fact]
    public async Task UserCannotAccessAnotherUsersVideoDetail()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Uploaded
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.GetVideoDetailAsync(10, 2);

        Assert.False(response.Success);
    }

    [Fact]
    public async Task UserCannotAccessAnotherUsersJobStatus()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        var video = new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Uploaded
        };
        dbContext.Videos.Add(video);
        dbContext.AnalysisJobs.Add(new AnalysisJob
        {
            Id = 20,
            Video = video,
            Status = JobStatus.Queued,
            CurrentStep = "Waiting"
        });
        await dbContext.SaveChangesAsync();
        var service = new JobService(dbContext, new FakeAnalysisJobQueue(), new FakeJobLogService());

        var response = await service.GetStatusByVideoIdAsync(10, 2);

        Assert.False(response.Success);
    }

    [Fact]
    public async Task HistoryOnlyReturnsCurrentUsersVideos()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        dbContext.Videos.AddRange(
            new Video { UserId = 1, OriginalName = "one.mp4", FileUrl = "one", FileSize = 1, Status = VideoStatus.Uploaded },
            new Video { UserId = 2, OriginalName = "two.mp4", FileUrl = "two", FileSize = 1, Status = VideoStatus.Uploaded },
            new Video { UserId = 1, OriginalName = "deleted.mp4", FileUrl = "deleted", FileSize = 1, Status = VideoStatus.Deleted, DeletedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.GetHistoryAsync(1, 1, 20, null);

        Assert.True(response.Success);
        Assert.Single(response.Data!.Items);
        Assert.Equal("one.mp4", response.Data.Items[0].OriginalName);
    }

    [Fact]
    public async Task UserCannotAccessAnotherUsersAnalysis()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed,
            AiResults =
            [
                new AiResult
                {
                    VisualScore = 0.5m,
                    FinalScore = 0.5m,
                    Confidence = 0.8m,
                    Label = AnalysisLabel.Suspicious,
                    RawModelOutputJson = "{}"
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.GetAnalysisAsync(10, 2);

        Assert.False(response.Success);
    }

    [Fact]
    public async Task NoAnalysisResultReturnsCleanError()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.GetAnalysisAsync(10, 1);

        Assert.False(response.Success);
        Assert.Contains("not available yet", response.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OwnerCanAccessAnalysisResult()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed,
            AiResults =
            [
                new AiResult
                {
                    VisualScore = 0.6m,
                    FinalScore = 0.55m,
                    Confidence = 0.8m,
                    Label = AnalysisLabel.Suspicious,
                    RawModelOutputJson = "{}",
                    EvidenceItems =
                    [
                        new EvidenceItem
                        {
                            Type = EvidenceType.SystemNote,
                            Severity = EvidenceSeverity.Low,
                            Title = "Mock AI service result",
                            Description = "Mock"
                        }
                    ]
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.GetAnalysisAsync(10, 1);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal("Suspicious", response.Data.Label);
        Assert.Single(response.Data.EvidenceItems);
    }

    [Fact]
    public async Task GetAnalysisReturnsLatestAiResult()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed,
            AiResults =
            [
                new AiResult
                {
                    VisualScore = 0.80m,
                    FinalScore = 0.80m,
                    Confidence = 0.80m,
                    Label = AnalysisLabel.LikelyAiGenerated,
                    RawModelOutputJson = "{}",
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
                },
                new AiResult
                {
                    VisualScore = 0.20m,
                    FinalScore = 0.20m,
                    Confidence = 0.90m,
                    Label = AnalysisLabel.LikelyReal,
                    RawModelOutputJson = "{}",
                    CreatedAt = DateTimeOffset.UtcNow
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.GetAnalysisAsync(10, 1);

        Assert.True(response.Success);
        Assert.Equal("LikelyReal", response.Data!.Label);
        Assert.Equal(0.20m, response.Data.FinalScore);
    }

    [Fact]
    public async Task ReanalyzeQueuesNewJobWithoutDeletingOldResult()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed,
            AiResults =
            [
                new AiResult
                {
                    VisualScore = 0.60m,
                    FinalScore = 0.55m,
                    Confidence = 0.80m,
                    Label = AnalysisLabel.Suspicious,
                    RawModelOutputJson = "{}"
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.ReanalyzeAsync(10, 1);

        Assert.True(response.Success);
        Assert.Equal(1, queue.EnqueueCalls);
        Assert.Equal(1, await dbContext.AiResults.CountAsync());
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
    }

    [Fact]
    public async Task ReanalyzeReturnsActiveJobWithoutDuplicateReservationOrEnqueue()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Queued,
            AnalysisJobs = [new AnalysisJob { Id = 20, Status = JobStatus.Queued, CurrentStep = "Waiting" }]
        });
        await dbContext.SaveChangesAsync();
        var entitlement = new FakeEntitlementService();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue, entitlement);

        var response = await service.ReanalyzeAsync(10, 1);

        Assert.True(response.Success);
        Assert.Equal(20, response.Data!.JobId);
        Assert.Equal(0, entitlement.ReserveCalls);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
    }

    [Fact]
    public async Task ReanalyzeQueueFailureReleasesReservationAndRestoresPreviousVideoStatus()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Completed
        });
        await dbContext.SaveChangesAsync();
        var entitlement = new FakeEntitlementService { NextReservationId = 51 };
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage, new FailingAnalysisJobQueue(), entitlement);

        var response = await service.ReanalyzeAsync(10, 1);

        Assert.False(response.Success);
        Assert.Equal(VideoInfrastructureErrorCodes.AnalysisQueueUnavailable, response.ErrorCode);
        Assert.Equal(1, entitlement.ReleaseCalls);
        Assert.Equal(0, storage.DeleteCalls);
        var video = await dbContext.Videos.SingleAsync();
        Assert.Equal(VideoStatus.Completed, video.Status);
        Assert.Equal("videos/1/file.mp4", video.FileUrl);
        var job = await dbContext.AnalysisJobs.SingleAsync();
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal(VideoInfrastructureErrorCodes.AnalysisQueueUnavailable, job.ErrorCode);
    }

    [Fact]
    public async Task ReanalyzeRejectsHistoricalVideoAboveCurrentAbsoluteLimit()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "old-large.mp4",
            FileUrl = "videos/1/old-large.mp4",
            FileSize = VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes + 1,
            Status = VideoStatus.Completed
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.ReanalyzeAsync(10, 1);

        Assert.False(response.Success);
        Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, response.ErrorCode);
        Assert.Equal("Videos larger than 300 MB are not supported at this time.", response.Message);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task OwnerCanRetryFailedAnalysisWithoutCreatingVideo()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Failed,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Failed,
                    Progress = 78,
                    ErrorCode = "BITMIND_UNAVAILABLE",
                    ErrorMessage = "External BitMind verification failed: BitMind returned HTTP 401.",
                    RetryCount = 0,
                    MaxRetryCount = 3
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.RetryAnalysisAsync(10, 1);

        Assert.True(response.Success);
        Assert.Equal(1, await dbContext.Videos.CountAsync());
        Assert.Equal(2, await dbContext.AnalysisJobs.CountAsync());
        Assert.Equal(1, queue.EnqueueCalls);
        Assert.Equal(1, response.Data!.RetryCount);
        Assert.Equal(3, response.Data.MaxRetryCount);
        Assert.Equal(10, response.Data.VideoId);
    }

    [Fact]
    public async Task RetryRejectsHistoricalVideoAboveCurrentAbsoluteLimit()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "old-large.mp4",
            FileUrl = "videos/1/old-large.mp4",
            FileSize = VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes + 1,
            Status = VideoStatus.Failed,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Failed,
                    RetryCount = 0,
                    MaxRetryCount = 3
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.RetryAnalysisAsync(10, 1);

        Assert.False(response.Success);
        Assert.Equal(SubscriptionErrorCodes.VideoSizeLimitExceeded, response.ErrorCode);
        Assert.Equal("Videos larger than 300 MB are not supported at this time.", response.Message);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task RetryQueueFailureReleasesReservationAndKeepsOriginalVideoAvailable()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Failed,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Failed,
                    RetryCount = 0,
                    MaxRetryCount = 3,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var entitlement = new FakeEntitlementService { NextReservationId = 52 };
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage, new FailingAnalysisJobQueue(), entitlement);

        var response = await service.RetryAnalysisAsync(10, 1);

        Assert.False(response.Success);
        Assert.Equal(VideoInfrastructureErrorCodes.AnalysisQueueUnavailable, response.ErrorCode);
        Assert.Equal(1, entitlement.ReleaseCalls);
        Assert.Equal(0, storage.DeleteCalls);
        var video = await dbContext.Videos.SingleAsync();
        Assert.Equal(VideoStatus.Failed, video.Status);
        Assert.Equal("videos/1/file.mp4", video.FileUrl);
        var latestJob = await dbContext.AnalysisJobs.OrderByDescending(job => job.Id).FirstAsync();
        Assert.Equal(JobStatus.Failed, latestJob.Status);
        Assert.Equal(VideoInfrastructureErrorCodes.AnalysisQueueUnavailable, latestJob.ErrorCode);
    }

    [Fact]
    public async Task NonOwnerCannotRetryAnotherUsersVideo()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Failed,
            AnalysisJobs = [new AnalysisJob { Status = JobStatus.Failed, RetryCount = 0, MaxRetryCount = 3 }]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.RetryAnalysisAsync(10, 2);

        Assert.False(response.Success);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
    }

    [Fact]
    public async Task RetryReturnsActiveJobWithoutDuplicateEnqueue()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Queued,
            AnalysisJobs =
            [
                new AnalysisJob { Id = 20, Status = JobStatus.Failed, RetryCount = 0, MaxRetryCount = 3, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2) },
                new AnalysisJob { Id = 21, Status = JobStatus.Queued, RetryCount = 1, MaxRetryCount = 3, CreatedAt = DateTimeOffset.UtcNow }
            ]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.RetryAnalysisAsync(10, 1);

        Assert.True(response.Success);
        Assert.Equal(21, response.Data!.JobId);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(2, await dbContext.AnalysisJobs.CountAsync());
    }

    [Fact]
    public async Task RetryRespectsMaxRetryCount()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Failed,
            AnalysisJobs = [new AnalysisJob { Status = JobStatus.Failed, RetryCount = 3, MaxRetryCount = 3 }]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.RetryAnalysisAsync(10, 1);

        Assert.False(response.Success);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
    }

    [Fact]
    public async Task RetryClearsStalePauseState()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Failed,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Status = JobStatus.Failed,
                    RetryCount = 1,
                    MaxRetryCount = 3,
                    PauseRequested = true,
                    PauseRequestedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
                    PausedAt = DateTimeOffset.UtcNow.AddMinutes(-3),
                    PausedFromStage = "Detailed scan",
                    LastCheckpoint = "before-segment-2",
                    ResumeBackgroundJobId = "stale"
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.RetryAnalysisAsync(10, 1);

        Assert.True(response.Success);
        var retryJob = await dbContext.AnalysisJobs.OrderBy(job => job.Id).LastAsync();
        Assert.Equal(JobStatus.Queued, retryJob.Status);
        Assert.False(retryJob.PauseRequested);
        Assert.Null(retryJob.PauseRequestedAt);
        Assert.Null(retryJob.PausedAt);
        Assert.Null(retryJob.PausedFromStage);
        Assert.Null(retryJob.LastCheckpoint);
        Assert.Null(retryJob.ResumeBackgroundJobId);
    }

    [Fact]
    public async Task PauseProcessingJobPersistsPauseRequestWithoutRetryIncrement()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Processing,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Processing,
                    CurrentStep = "Analyzing segment 2",
                    Progress = 35,
                    RetryCount = 1,
                    MaxRetryCount = 3
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.PauseAnalysisAsync(10, 1);

        Assert.True(response.Success);
        Assert.Equal(JobStatus.PauseRequested.ToString(), response.Data!.Status);
        var job = await dbContext.AnalysisJobs.SingleAsync();
        Assert.Equal(JobStatus.PauseRequested, job.Status);
        Assert.True(job.PauseRequested);
        Assert.NotNull(job.PauseRequestedAt);
        Assert.Equal(1, job.RetryCount);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task PauseQueuedJobPersistsPauseRequest()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Queued,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Queued,
                    CurrentStep = "Waiting for worker",
                    Progress = 0
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService(), new FakeAnalysisJobQueue());

        var response = await service.PauseAnalysisAsync(10, 1);

        Assert.True(response.Success);
        var job = await dbContext.AnalysisJobs.SingleAsync();
        Assert.Equal(JobStatus.PauseRequested, job.Status);
        Assert.True(job.PauseRequested);
    }

    [Fact]
    public async Task NonOwnerCannotPauseOrResumeAnotherUsersVideo()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Processing,
            AnalysisJobs = [new AnalysisJob { Id = 20, Status = JobStatus.Processing }]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var pauseResponse = await service.PauseAnalysisAsync(10, 2);
        var resumeResponse = await service.ResumeAnalysisAsync(10, 2);

        Assert.False(pauseResponse.Success);
        Assert.False(resumeResponse.Success);
        Assert.Equal(JobStatus.Processing, (await dbContext.AnalysisJobs.SingleAsync()).Status);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task ResumePausedJobQueuesSameAttemptOnce()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Queued,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Paused,
                    CurrentStep = "Paused before analyzing part 3 of 8",
                    Progress = 40,
                    PausedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                    PausedFromStage = "Detailed scan",
                    LastCheckpoint = "before-segment-3"
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var firstResponse = await service.ResumeAnalysisAsync(10, 1);
        var duplicateResponse = await service.ResumeAnalysisAsync(10, 1);

        Assert.True(firstResponse.Success);
        Assert.True(duplicateResponse.Success);
        var job = await dbContext.AnalysisJobs.SingleAsync();
        Assert.Equal(20, job.Id);
        Assert.Equal(JobStatus.ResumeRequested, job.Status);
        Assert.False(job.PauseRequested);
        Assert.Null(job.PauseRequestedAt);
        Assert.NotNull(job.ResumedAt);
        Assert.Equal("fake-20", job.ResumeBackgroundJobId);
        Assert.Equal(1, queue.ResumeCalls);
        Assert.Equal(1, queue.EnqueueCalls);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Processing)]
    public async Task InvalidResumeStatesAreRejected(JobStatus status)
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Uploaded,
            AnalysisJobs = [new AnalysisJob { Id = 20, Status = status }]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.ResumeAnalysisAsync(10, 1);

        Assert.False(response.Success);
        Assert.Equal(status, (await dbContext.AnalysisJobs.SingleAsync()).Status);
        Assert.Equal(0, queue.EnqueueCalls);
    }

    [Fact]
    public async Task CancelPausedJobMarksCancelledWithoutRequeueing()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Queued,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Paused,
                    Progress = 55,
                    PauseRequested = true,
                    PausedAt = DateTimeOffset.UtcNow,
                    ResumeBackgroundJobId = "old",
                    Segments =
                    [
                        new AnalysisSegment { SegmentIndex = 0, Status = AnalysisSegmentStatus.Completed },
                        new AnalysisSegment { SegmentIndex = 1, Status = AnalysisSegmentStatus.Pending }
                    ]
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var queue = new FakeAnalysisJobQueue();
        var service = CreateService(dbContext, new FakeObjectStorageService(), queue);

        var response = await service.CancelAnalysisAsync(10, 1);

        Assert.True(response.Success);
        var job = await dbContext.AnalysisJobs.Include(existingJob => existingJob.Segments).SingleAsync();
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.False(job.PauseRequested);
        Assert.Null(job.PauseRequestedAt);
        Assert.Null(job.ResumeBackgroundJobId);
        Assert.Equal(0, queue.EnqueueCalls);
        Assert.Contains(job.Segments, segment => segment.Status == AnalysisSegmentStatus.Cancelled);
    }

    [Fact]
    public async Task JobStatusMapsBitMindFailureToSafeUserMessage()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Failed,
            AnalysisJobs =
            [
                new AnalysisJob
                {
                    Id = 20,
                    Status = JobStatus.Failed,
                    ErrorCode = "BITMIND_UNAVAILABLE",
                    ErrorMessage = "External BitMind verification failed: BitMind returned HTTP 401. Error code: BITMIND_UNAVAILABLE",
                    RetryCount = 0,
                    MaxRetryCount = 3
                }
            ]
        });
        await dbContext.SaveChangesAsync();
        var service = new JobService(dbContext, new FakeAnalysisJobQueue(), new FakeJobLogService());

        var response = await service.GetStatusByVideoIdAsync(10, 1);

        Assert.True(response.Success);
        Assert.Equal("ProviderAuthenticationFailed", response.Data!.ErrorCode);
        Assert.Equal("The external analysis service is temporarily unavailable. Please try again later.", response.Data.UserMessage);
        Assert.DoesNotContain("HTTP 401", response.Data.ErrorMessage);
        Assert.Equal("JOB-20", response.Data.TechnicalReferenceId);
        Assert.True(response.Data.CanRetry);
    }

    private static VideoService CreateService(
        AppDbContext dbContext,
        IObjectStorageService storage,
        IAnalysisJobQueue queue,
        IEntitlementService? entitlementService = null,
        IDeviceIdentityService? deviceIdentityService = null,
        IMetadataExtractionService? metadataExtractionService = null,
        IVideoStorageCapacityService? storageCapacityService = null,
        IVideoWorkloadGate? workloadGate = null,
        VideoStorageProtectionOptions? storageProtectionOptions = null)
    {
        var validator = new UploadVideoRequestValidator(Options.Create(new VideoUploadOptions()));
        return new VideoService(
            dbContext,
            storage,
            queue,
            new FakeJobLogService(),
            entitlementService ?? new FakeEntitlementService(),
            deviceIdentityService ?? new FakeDeviceIdentityService(),
            metadataExtractionService ?? new FakeMetadataExtractionService(),
            storageCapacityService ?? new FakeVideoStorageCapacityService(),
            workloadGate ?? new FakeVideoWorkloadGate(),
            validator,
            Options.Create(new VideoUploadOptions()),
            Options.Create(storageProtectionOptions ?? new VideoStorageProtectionOptions
            {
                UploadTempRootPath = Path.Combine(Path.GetTempPath(), "ai-video-upload-tests", Guid.NewGuid().ToString("N"))
            }),
            Options.Create(new VideoProcessingOptions()),
            NullLogger<VideoService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(CreateOptions());
    }

    private static DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static User CreateUser(long id, string email)
    {
        return new User
        {
            Id = id,
            Name = "Test User",
            Email = email,
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true
        };
    }

    private static UploadVideoRequest CreateUploadRequest(
        byte[]? content = null,
        AnalysisMode analysisMode = AnalysisMode.Basic,
        long? declaredLength = null,
        string fileName = "sample.mp4",
        string contentType = "video/mp4")
    {
        var stream = new MemoryStream(content ?? CreateMp4Header());
        return new UploadVideoRequest
        {
            File = new FormFile(stream, 0, declaredLength ?? stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            },
            ConsentAccepted = true,
            AnalysisMode = analysisMode
        };
    }

    private static byte[] CreateMp4Header()
    {
        return
        [
            0x00, 0x00, 0x00, 0x18,
            (byte)'f', (byte)'t', (byte)'y', (byte)'p',
            (byte)'i', (byte)'s', (byte)'o', (byte)'m',
            0x00, 0x00, 0x02, 0x00
        ];
    }

    private static byte[] CreateVideoHeader(string signatureKind)
    {
        return signatureKind switch
        {
            "mp4" => CreateMp4Header(),
            "mov" =>
            [
                0x00, 0x00, 0x00, 0x14,
                (byte)'f', (byte)'t', (byte)'y', (byte)'p',
                (byte)'q', (byte)'t', 0x20, 0x20,
                0x00, 0x00, 0x00, 0x00
            ],
            "mov-legacy" =>
            [
                0x00, 0x00, 0x00, 0x10,
                (byte)'m', (byte)'o', (byte)'o', (byte)'v',
                0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00
            ],
            "avi" =>
            [
                (byte)'R', (byte)'I', (byte)'F', (byte)'F',
                0x10, 0x00, 0x00, 0x00,
                (byte)'A', (byte)'V', (byte)'I', 0x20
            ],
            "ebml" => [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81],
            _ => throw new ArgumentOutOfRangeException(nameof(signatureKind), signatureKind, null)
        };
    }

    private sealed class FakeMetadataExtractionService : IMetadataExtractionService
    {
        public bool HasVideoStream { get; init; }

        public Task<ExtractedMetadataResult> ExtractMetadataAsync(VideoProcessingInput input, CancellationToken cancellationToken)
        {
            if (!HasVideoStream)
            {
                throw new ProcessingException("FFPROBE_FAILED", "Video metadata could not be read.");
            }

            return Task.FromResult(new ExtractedMetadataResult(
                "avi",
                "mpeg4",
                null,
                30,
                "640x360",
                1,
                100_000,
                null,
                null,
                false,
                [],
                "{}"));
        }
    }

    private sealed class FakeObjectStorageService : IObjectStorageService
    {
        public int UploadCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public Task<string> UploadAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            return Task.FromResult(objectKey);
        }

        public Task DeleteAsync(string objectKeyOrUrl, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

        public Task<string> GetReadUrlAsync(string objectKeyOrUrl, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(objectKeyOrUrl);
        }

        public Task DownloadToAsync(string objectKeyOrUrl, string destinationPath, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAnalysisJobQueue : IAnalysisJobQueue
    {
        public int EnqueueCalls { get; private set; }

        public int ResumeCalls { get; private set; }

        public string EnqueueAnalysisJob(long jobId)
        {
            EnqueueCalls++;
            return $"fake-{jobId}";
        }

        public string RetryAnalysisJob(long jobId)
        {
            return EnqueueAnalysisJob(jobId);
        }

        public string ResumeAnalysisJob(long jobId)
        {
            ResumeCalls++;
            return EnqueueAnalysisJob(jobId);
        }
    }

    private sealed class FailingAnalysisJobQueue : IAnalysisJobQueue
    {
        public string EnqueueAnalysisJob(long jobId)
        {
            throw new InvalidOperationException("Hangfire enqueue failed.");
        }

        public string RetryAnalysisJob(long jobId)
        {
            return EnqueueAnalysisJob(jobId);
        }

        public string ResumeAnalysisJob(long jobId)
        {
            return EnqueueAnalysisJob(jobId);
        }
    }

    private sealed class FakeJobLogService : IJobLogService
    {
        public Task LogAsync(
            long jobId,
            string stepName,
            string level,
            string message,
            object? details = null,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FakeVideoStorageCapacityService : IVideoStorageCapacityService
    {
        public VideoStorageCapacityCheckResult UploadResult { get; init; } = VideoStorageCapacityCheckResult.Succeeded();

        public VideoStorageCapacityCheckResult ProcessingResult { get; init; } = VideoStorageCapacityCheckResult.Succeeded();

        public Task<VideoStorageCapacityCheckResult> CheckUploadCapacityAsync(
            long originalFileSizeBytes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(UploadResult);
        }

        public Task<VideoStorageCapacityCheckResult> CheckProcessingCapacityAsync(
            long originalFileSizeBytes,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ProcessingResult);
        }
    }

    private sealed class FakeVideoWorkloadGate : IVideoWorkloadGate
    {
        public Task<IAsyncDisposable?> TryEnterUploadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable?>(new NoopAsyncDisposable());
        }

        public Task<IAsyncDisposable> EnterProcessingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable>(new NoopAsyncDisposable());
        }
    }

    private sealed class RejectingVideoWorkloadGate : IVideoWorkloadGate
    {
        public Task<IAsyncDisposable?> TryEnterUploadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable?>(null);
        }

        public Task<IAsyncDisposable> EnterProcessingAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable>(new NoopAsyncDisposable());
        }
    }

    private sealed class NoopAsyncDisposable : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeDeviceIdentityService : IDeviceIdentityService
    {
        public Task<SubscriptionClientContext> ResolveAsync(long userId, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new SubscriptionClientContext
            {
                DeviceIdentityId = 1,
                IpHash = "ip-hash"
            });
        }
    }

    private sealed class FakeEntitlementService : IEntitlementService
    {
        public int ReserveCalls { get; private set; }

        public int ReleaseCalls { get; private set; }

        public long? NextReservationId { get; init; }

        public ScanReservationResult? NextResult { get; init; }

        public Task<ApiResponse<SubscriptionStatusResponse>> GetStatusAsync(
            long userId,
            SubscriptionClientContext? clientContext = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<SubscriptionStatusResponse>.SuccessResponse(new SubscriptionStatusResponse
            {
                PlanCode = SubscriptionPlanCodes.Pro,
                PlanName = "Pro",
                SubscriptionStatus = SubscriptionStatuses.Active,
                AllowsSmartScan = true,
                AllowsDetailedScan = true
            }));
        }

        public Task<ScanReservationResult> ReserveScanAsync(
            ScanReservationRequest request,
            CancellationToken cancellationToken = default)
        {
            ReserveCalls++;
            return Task.FromResult(NextResult ?? ScanReservationResult.Succeeded(
                "Reserved.",
                ScanReservationKinds.PaidSubscription,
                NextReservationId,
                remainingScans: 24));
        }

        public Task<ScanReservationResult> ConsumeReservationAsync(
            long reservationId,
            long? videoId = null,
            long? analysisJobId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ScanReservationResult.Succeeded(
                "Consumed.",
                ScanReservationKinds.PaidSubscription,
                reservationId,
                remainingScans: null));
        }

        public Task<ScanReservationResult> ReleaseReservationAsync(
            long reservationId,
            string? reason = null,
            CancellationToken cancellationToken = default)
        {
            ReleaseCalls++;
            return Task.FromResult(ScanReservationResult.Succeeded(
                "Released.",
                ScanReservationKinds.PaidSubscription,
                reservationId,
                remainingScans: null));
        }
    }

    private sealed class FailingSaveAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Database save failed.");
        }
    }
}
