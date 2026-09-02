using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Validators;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos;
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
        IAnalysisJobQueue queue)
    {
        var validator = new UploadVideoRequestValidator(Options.Create(new VideoUploadOptions()));
        return new VideoService(
            dbContext,
            storage,
            queue,
            new FakeJobLogService(),
            validator,
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
        AnalysisMode analysisMode = AnalysisMode.Basic)
    {
        var stream = new MemoryStream(content ?? CreateMp4Header());
        return new UploadVideoRequest
        {
            File = new FormFile(stream, 0, stream.Length, "file", "sample.mp4")
            {
                Headers = new HeaderDictionary(),
                ContentType = "video/mp4"
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

    private sealed class FailingSaveAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Database save failed.");
        }
    }
}
