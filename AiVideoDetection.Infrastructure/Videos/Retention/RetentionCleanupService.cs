using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Retention;

public class RetentionCleanupService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IOptions<VideoProcessingOptions> options,
    ILogger<RetentionCleanupService> logger) : IRetentionCleanupService
{
    private const int BatchSize = 100;
    private readonly VideoProcessingOptions _options = options.Value;

    public async Task CleanupTemporaryFilesAsync()
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(_options.TemporaryFileRetention);

        CleanupWorkingDirectories(cutoff);
        await CleanupStoredFrameImagesAsync(cutoff);
    }

    public async Task CleanupExpiredRetainedAssetsAsync()
    {
        var now = DateTimeOffset.UtcNow;

        await CleanupExpiredOriginalMediaAsync(now);
        await CleanupExpiredDetailedResultsAsync(now.Subtract(_options.DetailedResultRetention));
    }

    private void CleanupWorkingDirectories(DateTimeOffset cutoff)
    {
        var rootPath = Path.GetFullPath(_options.WorkingRootPath);
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            try
            {
                var fullPath = Path.GetFullPath(directory);
                if (!IsInsideDirectory(fullPath, rootPath))
                {
                    logger.LogWarning("Skipped unsafe retention cleanup path {Path}.", fullPath);
                    continue;
                }

                if (Directory.GetLastWriteTimeUtc(fullPath) > cutoff.UtcDateTime)
                {
                    continue;
                }

                Directory.Delete(fullPath, recursive: true);
                logger.LogInformation("Deleted expired video processing work directory {Path}.", fullPath);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to delete expired video processing work directory {Path}.", directory);
            }
        }
    }

    private async Task CleanupStoredFrameImagesAsync(DateTimeOffset cutoff)
    {
        while (true)
        {
            var frames = await dbContext.VideoFrames
                .Where(frame => frame.CreatedAt <= cutoff && frame.FrameUrl != string.Empty)
                .OrderBy(frame => frame.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (frames.Count == 0)
            {
                return;
            }

            foreach (var frame in frames)
            {
                await DeleteObjectQuietlyAsync(frame.FrameUrl, "frame image");
                frame.FrameUrl = string.Empty;
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupExpiredOriginalMediaAsync(DateTimeOffset now)
    {
        var fallbackCutoff = now.Subtract(_options.OriginalVideoRetention);

        while (true)
        {
            var videos = await dbContext.Videos
                .Where(video => video.FileUrl != string.Empty
                    && ((video.RetentionDeleteAt != null && video.RetentionDeleteAt <= now)
                        || (video.RetentionDeleteAt == null && video.CreatedAt <= fallbackCutoff)))
                .OrderBy(video => video.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (videos.Count == 0)
            {
                return;
            }

            foreach (var video in videos)
            {
                await DeleteObjectQuietlyAsync(video.FileUrl, "original video");
                if (!string.IsNullOrWhiteSpace(video.ThumbnailUrl))
                {
                    await DeleteObjectQuietlyAsync(video.ThumbnailUrl, "thumbnail");
                    video.ThumbnailUrl = null;
                }

                video.FileUrl = string.Empty;
                video.RetentionDeleteAt ??= video.CreatedAt.Add(_options.OriginalVideoRetention);
                video.UpdatedAt = now;
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupExpiredDetailedResultsAsync(DateTimeOffset cutoff)
    {
        await CleanupEvidenceAsync(cutoff);
        await CleanupSourceMatchesAsync(cutoff);
        await CleanupProviderPayloadsAsync(cutoff);
        await CleanupAnalysisPayloadsAsync(cutoff);
    }

    private async Task CleanupEvidenceAsync(DateTimeOffset cutoff)
    {
        while (true)
        {
            var evidence = await dbContext.EvidenceItems
                .Where(item => item.CreatedAt <= cutoff)
                .OrderBy(item => item.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (evidence.Count == 0)
            {
                return;
            }

            dbContext.EvidenceItems.RemoveRange(evidence);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupSourceMatchesAsync(DateTimeOffset cutoff)
    {
        while (true)
        {
            var matches = await dbContext.SourceMatches
                .Where(match => match.CreatedAt <= cutoff)
                .OrderBy(match => match.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (matches.Count == 0)
            {
                return;
            }

            dbContext.SourceMatches.RemoveRange(matches);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupProviderPayloadsAsync(DateTimeOffset cutoff)
    {
        while (true)
        {
            var requests = await dbContext.AiProviderRequests
                .Where(request => request.CreatedAt <= cutoff
                    && (request.RawRequestMetadataJson != null || request.RawResponseJson != null))
                .OrderBy(request => request.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (requests.Count == 0)
            {
                return;
            }

            foreach (var request in requests)
            {
                request.RawRequestMetadataJson = null;
                request.RawResponseJson = null;
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupAnalysisPayloadsAsync(DateTimeOffset cutoff)
    {
        while (true)
        {
            var results = await dbContext.AiResults
                .Where(result => result.CreatedAt <= cutoff
                    && (result.RawModelOutputJson != "{}"
                        || result.LocalResultJson != null
                        || result.HybridResultJson != null
                        || result.ExternalRawResponseJson != null))
                .OrderBy(result => result.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (results.Count == 0)
            {
                break;
            }

            foreach (var result in results)
            {
                result.RawModelOutputJson = "{}";
                result.LocalResultJson = null;
                result.HybridResultJson = null;
                result.ExternalRawResponseJson = null;
            }

            await dbContext.SaveChangesAsync();
        }

        while (true)
        {
            var segments = await dbContext.AnalysisSegments
                .Where(segment => segment.CreatedAt <= cutoff
                    && (segment.ResultJson != null || segment.LocalTemporaryPath != null))
                .OrderBy(segment => segment.Id)
                .Take(BatchSize)
                .ToListAsync();

            if (segments.Count == 0)
            {
                return;
            }

            foreach (var segment in segments)
            {
                segment.ResultJson = null;
                segment.LocalTemporaryPath = null;
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task DeleteObjectQuietlyAsync(string objectKey, string description)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return;
        }

        try
        {
            await objectStorageService.DeleteAsync(objectKey);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to delete expired {Description} object {ObjectKey}.", description, objectKey);
        }
    }

    private static bool IsInsideDirectory(string path, string rootPath)
    {
        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
