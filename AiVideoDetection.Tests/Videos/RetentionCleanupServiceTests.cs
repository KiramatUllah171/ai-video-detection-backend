using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class RetentionCleanupServiceTests
{
    [Fact]
    public async Task CleanupExpiredRetainedAssetsDeletesOriginalMediaButKeepsVideoSummary()
    {
        await using var dbContext = CreateDbContext();
        var now = DateTimeOffset.UtcNow;
        dbContext.Videos.Add(new Video
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
        });
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage);

        await service.CleanupExpiredRetainedAssetsAsync();

        var video = await dbContext.Videos.SingleAsync();
        Assert.Equal(VideoStatus.Completed, video.Status);
        Assert.Null(video.DeletedAt);
        Assert.Equal(string.Empty, video.FileUrl);
        Assert.Null(video.ThumbnailUrl);
        Assert.Contains("videos/1/expired.mp4", storage.DeletedObjects);
        Assert.Contains("thumbnails/1/expired.jpg", storage.DeletedObjects);
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
    }

    [Fact]
    public async Task CleanupExpiredRetainedAssetsDeletesDetailedResultDataAfterRetention()
    {
        await using var dbContext = CreateDbContext();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-31);
        var video = new Video
        {
            Id = 1,
            UserId = 1,
            OriginalName = "summary.mp4",
            FileUrl = string.Empty,
            FileSize = 100,
            Status = VideoStatus.Completed,
            CreatedAt = cutoff
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
            AnalysisJobId = 1,
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
            AnalysisJobId = 1,
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

        var preservedResult = await dbContext.AiResults.SingleAsync();
        Assert.Equal(0.2m, preservedResult.FinalScore);
        Assert.Equal("{}", preservedResult.RawModelOutputJson);
        Assert.Null(preservedResult.LocalResultJson);
        Assert.Null(preservedResult.HybridResultJson);
        Assert.Null(preservedResult.ExternalRawResponseJson);
        Assert.False(await dbContext.EvidenceItems.AnyAsync());
        Assert.False(await dbContext.SourceMatches.AnyAsync());
        Assert.Null((await dbContext.AiProviderRequests.SingleAsync()).RawResponseJson);
        Assert.Null((await dbContext.AnalysisSegments.SingleAsync()).ResultJson);
    }

    private static RetentionCleanupService CreateService(AppDbContext dbContext, IObjectStorageService storage)
    {
        return new RetentionCleanupService(
            dbContext,
            storage,
            Options.Create(new VideoProcessingOptions
            {
                WorkingRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
                TemporaryFileRetentionHours = 24,
                OriginalVideoRetentionDays = 3,
                ReportRetentionDays = 30,
                DetailedResultRetentionDays = 30
            }),
            NullLogger<RetentionCleanupService>.Instance);
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
