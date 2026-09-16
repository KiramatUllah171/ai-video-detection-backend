using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Common;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class RetentionCleanupServiceTests
{
    [Fact]
    public async Task CleanupExpiredRetainedAssetsHardDeletesExpiredVideoAndReportData()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        var video = new Video
        {
            Id = 1,
            UserId = 1,
            OriginalName = "expired.mp4",
            FileUrl = "videos/1/expired.mp4",
            ThumbnailUrl = "thumbnails/1/expired.jpg",
            FileSize = 100,
            Status = VideoStatus.Completed,
            CreatedAt = now.AddDays(-4),
            RetentionDeleteAt = now.AddDays(-1)
        };
        dbContext.Videos.Add(video);
        dbContext.AnalysisJobs.Add(new AnalysisJob
        {
            Id = 1,
            Video = video,
            VideoId = video.Id,
            Status = JobStatus.Completed,
            Progress = 100,
            CreatedAt = now.AddDays(-4),
            UpdatedAt = now.AddDays(-4)
        });
        dbContext.AiResults.Add(new AiResult
        {
            Id = 1,
            Video = video,
            VideoId = video.Id,
            VisualScore = 0.2m,
            FinalScore = 0.2m,
            Confidence = 0.8m,
            Label = AnalysisLabel.LikelyReal,
            CreatedAt = now.AddDays(-4)
        });
        dbContext.VideoFrames.Add(new VideoFrame
        {
            Id = 1,
            Video = video,
            VideoId = video.Id,
            FrameUrl = "frames/1/expired.jpg",
            FrameIndex = 1,
            TimestampSeconds = 1,
            CreatedAt = now.AddDays(-4)
        });
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage);

        await service.CleanupExpiredRetainedAssetsAsync();

        Assert.False(await dbContext.Videos.AnyAsync());
        Assert.False(await dbContext.AnalysisJobs.AnyAsync());
        Assert.False(await dbContext.AiResults.AnyAsync());
        Assert.False(await dbContext.VideoFrames.AnyAsync());
        Assert.Contains("videos/1/expired.mp4", storage.DeletedObjects);
        Assert.Contains("thumbnails/1/expired.jpg", storage.DeletedObjects);
        Assert.Contains("frames/1/expired.jpg", storage.DeletedObjects);

        var metric = await dbContext.RetentionCleanupRuns.SingleAsync();
        Assert.Equal("retained-assets", metric.JobName);
        Assert.Equal("Succeeded", metric.Status);
        Assert.Equal(1, metric.OriginalVideosCleared);
        Assert.Equal(1, metric.ThumbnailsCleared);
        Assert.Equal(1, metric.FrameObjectsCleared);
        Assert.Equal(1, metric.AnalysisPayloadsCleared);
        Assert.Equal(0, metric.FailureCount);
    }

    [Fact]
    public async Task CleanupTemporaryFilesClearsOnlyExpiredStoredFrameUrls()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.VideoFrames.AddRange(
            new VideoFrame
            {
                Id = 1,
                VideoId = 1,
                FrameUrl = "frames/old.jpg",
                FrameIndex = 1,
                TimestampSeconds = 1,
                CreatedAt = now.AddHours(-25)
            },
            new VideoFrame
            {
                Id = 2,
                VideoId = 1,
                FrameUrl = "frames/current.jpg",
                FrameIndex = 2,
                TimestampSeconds = 2,
                CreatedAt = now.AddHours(-2)
            });
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage);

        await service.CleanupTemporaryFilesAsync();

        var frames = await dbContext.VideoFrames.OrderBy(frame => frame.Id).ToListAsync();
        Assert.Equal(string.Empty, frames[0].FrameUrl);
        Assert.Equal("frames/current.jpg", frames[1].FrameUrl);
        Assert.Single(storage.DeletedObjects, "frames/old.jpg");

        var metric = await dbContext.RetentionCleanupRuns.SingleAsync();
        Assert.Equal("temporary-files", metric.JobName);
        Assert.Equal("Succeeded", metric.Status);
        Assert.Equal(1, metric.FrameObjectsCleared);
        Assert.Equal(0, metric.FailureCount);
    }

    [Fact]
    public async Task CleanupTemporaryFilesDeletesOnlyStaleUploadTempFiles()
    {
        await using var dbContext = CreateDbContext();
        var uploadTempRoot = Path.Combine(Path.GetTempPath(), "ai-video-upload-cleanup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(uploadTempRoot);
        var oldFile = Path.Combine(uploadTempRoot, "upload-old.mp4");
        var currentFile = Path.Combine(uploadTempRoot, "upload-current.mp4");
        await File.WriteAllBytesAsync(oldFile, [1]);
        await File.WriteAllBytesAsync(currentFile, [2]);
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-5));
        File.SetLastWriteTimeUtc(currentFile, DateTime.UtcNow);
        var service = CreateService(dbContext, new FakeObjectStorageService(), uploadTempRoot);

        await service.CleanupTemporaryFilesAsync();

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(currentFile));
        Assert.True(await dbContext.RetentionCleanupRuns.AnyAsync(run => run.JobName == "temporary-files" && run.Status == "Succeeded"));

        Directory.Delete(uploadTempRoot, recursive: true);
    }

    [Fact]
    public async Task CleanupExpiredRetainedAssetsDeletesExpiredVideoDatabaseRowsAfterRetention()
    {
        await using var dbContext = CreateDbContext();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-4);
        var video = new Video
        {
            Id = 1,
            UserId = 1,
            OriginalName = "summary.mp4",
            FileUrl = string.Empty,
            FileSize = 100,
            Status = VideoStatus.Completed,
            CreatedAt = cutoff,
            RetentionDeleteAt = cutoff.AddDays(3)
        };
        var job = new AnalysisJob
        {
            Id = 1,
            Video = video,
            VideoId = video.Id,
            Status = JobStatus.Completed,
            Progress = 100,
            CreatedAt = cutoff,
            UpdatedAt = cutoff
        };
        var result = new AiResult
        {
            Id = 1,
            Video = video,
            VideoId = video.Id,
            VisualScore = 0.2m,
            FinalScore = 0.2m,
            Confidence = 0.8m,
            Label = AnalysisLabel.LikelyReal,
            RawModelOutputJson = "{\"large\":true}",
            LocalResultJson = "{\"large\":true}",
            HybridResultJson = "{\"large\":true}",
            ExternalRawResponseJson = "{\"large\":true}",
            CreatedAt = cutoff
        };
        dbContext.Videos.Add(video);
        dbContext.AnalysisJobs.Add(job);
        dbContext.AiResults.Add(result);
        dbContext.EvidenceItems.Add(new EvidenceItem
        {
            Id = 1,
            AiResult = result,
            AiResultId = result.Id,
            Type = EvidenceType.MetadataWarning,
            Severity = EvidenceSeverity.Low,
            Title = "Old evidence",
            Description = "Old evidence",
            CreatedAt = cutoff
        });
        dbContext.SourceMatches.Add(new SourceMatch
        {
            Id = 1,
            Video = video,
            VideoId = video.Id,
            Platform = "Internal",
            SimilarityScore = 1,
            Confidence = ConfidenceLevel.High,
            CreatedAt = cutoff
        });
        dbContext.AiProviderRequests.Add(new AiProviderRequest
        {
            Id = 1,
            Video = video,
            VideoId = video.Id,
            AnalysisJob = job,
            AnalysisJobId = job.Id,
            UserId = 1,
            ProviderName = "BitMind",
            ProviderMode = "hybrid",
            Status = "Completed",
            RequestStartedAt = cutoff,
            RawRequestMetadataJson = "{\"large\":true}",
            RawResponseJson = "{\"large\":true}",
            CreatedAt = cutoff
        });
        dbContext.AnalysisSegments.Add(new AnalysisSegment
        {
            Id = 1,
            AnalysisJob = job,
            AnalysisJobId = job.Id,
            Video = video,
            VideoId = video.Id,
            SegmentIndex = 1,
            StartTime = 0,
            EndTime = 1,
            Duration = 1,
            ResultJson = "{\"large\":true}",
            LocalTemporaryPath = "storage/work/old.mp4",
            CreatedAt = cutoff
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService());

        await service.CleanupExpiredRetainedAssetsAsync();

        Assert.False(await dbContext.Videos.AnyAsync());
        Assert.False(await dbContext.AnalysisJobs.AnyAsync());
        Assert.False(await dbContext.AiResults.AnyAsync());
        Assert.False(await dbContext.EvidenceItems.AnyAsync());
        Assert.False(await dbContext.SourceMatches.AnyAsync());
        Assert.False(await dbContext.AiProviderRequests.AnyAsync());
        Assert.False(await dbContext.AnalysisSegments.AnyAsync());

        var metric = await dbContext.RetentionCleanupRuns.SingleAsync();
        Assert.Equal("retained-assets", metric.JobName);
        Assert.Equal("Succeeded", metric.Status);
        Assert.Equal(1, metric.OriginalVideosCleared);
        Assert.Equal(1, metric.EvidenceRowsDeleted);
        Assert.Equal(1, metric.SourceMatchRowsDeleted);
        Assert.Equal(1, metric.ProviderPayloadsCleared);
        Assert.Equal(1, metric.AnalysisPayloadsCleared);
        Assert.Equal(1, metric.SegmentPayloadsCleared);
    }

    private static RetentionCleanupService CreateService(
        AppDbContext dbContext,
        IObjectStorageService storage,
        string? uploadTempRoot = null)
    {
        return new RetentionCleanupService(
            dbContext,
            storage,
            Options.Create(new VideoProcessingOptions
            {
                WorkingRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                TemporaryFileRetentionHours = 24,
                OriginalVideoRetentionDays = 3,
                ReportRetentionDays = 3,
                DetailedResultRetentionDays = 3
            }),
            Options.Create(new VideoStorageProtectionOptions
            {
                UploadTempRootPath = uploadTempRoot ?? Path.Combine(Path.GetTempPath(), "ai-video-upload-cleanup-tests", Guid.NewGuid().ToString("N")),
                OrphanUploadTempRetentionHours = 4
            }),
            CreateAlertService(),
            NullLogger<RetentionCleanupService>.Instance);
    }

    private static LoggingMonitoringAlertService CreateAlertService()
    {
        return new LoggingMonitoringAlertService(
            Options.Create(new MonitoringOptions()),
            NullLogger<LoggingMonitoringAlertService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private sealed class FakeObjectStorageService : IObjectStorageService
    {
        public List<string> DeletedObjects { get; } = [];

        public Task<string> UploadAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(objectKey);
        }

        public Task DeleteAsync(string objectKeyOrUrl, CancellationToken cancellationToken = default)
        {
            DeletedObjects.Add(objectKeyOrUrl);
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
}
