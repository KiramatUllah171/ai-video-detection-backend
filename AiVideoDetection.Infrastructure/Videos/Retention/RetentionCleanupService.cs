using System.Diagnostics;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Retention;

public class RetentionCleanupService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IOptions<VideoProcessingOptions> options,
    IOptions<VideoStorageProtectionOptions> storageProtectionOptions,
    IMonitoringAlertService monitoringAlertService,
    ILogger<RetentionCleanupService> logger) : IRetentionCleanupService
{
    private const int BatchSize = 100;
    private readonly VideoProcessingOptions _options = options.Value;
    private readonly VideoStorageProtectionOptions _storageProtectionOptions = storageProtectionOptions.Value;

    public async Task CleanupTemporaryFilesAsync()
    {
        var metrics = new CleanupMetrics();
        await RunTrackedCleanupAsync("temporary-files", metrics, async () =>
        {
            var cutoff = DateTimeOffset.UtcNow.Subtract(_options.TemporaryFileRetention);
            await CleanupWorkingDirectoriesAsync(cutoff, metrics);
            await CleanupOrphanUploadTemporaryFilesAsync(DateTimeOffset.UtcNow.Subtract(_storageProtectionOptions.OrphanUploadTempRetention), metrics);
            await CleanupStoredFrameImagesAsync(cutoff, metrics);
        });
    }

    public async Task CleanupExpiredRetainedAssetsAsync()
    {
        var metrics = new CleanupMetrics();
        await RunTrackedCleanupAsync("retained-assets", metrics, async () =>
        {
            var now = DateTimeOffset.UtcNow;
            await CleanupExpiredOriginalMediaAsync(now, metrics);
            await CleanupExpiredDetailedResultsAsync(now.Subtract(_options.DetailedResultRetention), metrics);
        });
    }

    private async Task RunTrackedCleanupAsync(string jobName, CleanupMetrics metrics, Func<Task> cleanup)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var status = "Succeeded";

        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            status = "Failed";
            metrics.FailureCount++;
            metrics.ErrorMessage = exception.Message;
            logger.LogError(exception, "Retention cleanup job {JobName} failed.", jobName);
            await monitoringAlertService.RecordCleanupFailureAsync(jobName, null, exception.Message, CancellationToken.None);
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await SaveCleanupRunMetricAsync(jobName, status, startedAt, stopwatch.ElapsedMilliseconds, metrics);
        }
    }

    private async Task SaveCleanupRunMetricAsync(
        string jobName,
        string status,
        DateTimeOffset startedAt,
        long durationMs,
        CleanupMetrics metrics)
    {
        try
        {
            dbContext.RetentionCleanupRuns.Add(new RetentionCleanupRun
            {
                JobName = jobName,
                Status = metrics.FailureCount > 0 && status == "Succeeded" ? "SucceededWithWarnings" : status,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.UtcNow,
                DurationMs = durationMs,
                WorkDirectoriesDeleted = metrics.WorkDirectoriesDeleted,
                FrameObjectsCleared = metrics.FrameObjectsCleared,
                OriginalVideosCleared = metrics.OriginalVideosCleared,
                ThumbnailsCleared = metrics.ThumbnailsCleared,
                EvidenceRowsDeleted = metrics.EvidenceRowsDeleted,
                SourceMatchRowsDeleted = metrics.SourceMatchRowsDeleted,
                ProviderPayloadsCleared = metrics.ProviderPayloadsCleared,
                AnalysisPayloadsCleared = metrics.AnalysisPayloadsCleared,
                SegmentPayloadsCleared = metrics.SegmentPayloadsCleared,
                FailureCount = metrics.FailureCount,
                ErrorMessage = metrics.ErrorMessage
            });
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to save retention cleanup metric for job {JobName}.", jobName);
        }
    }

    private async Task CleanupWorkingDirectoriesAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
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
                metrics.WorkDirectoriesDeleted++;
                logger.LogInformation("Deleted expired video processing work directory {Path}.", fullPath);
            }
            catch (Exception exception)
            {
                metrics.FailureCount++;
                metrics.ErrorMessage ??= exception.Message;
                logger.LogWarning(exception, "Failed to delete expired video processing work directory {Path}.", directory);
                await monitoringAlertService.RecordCleanupFailureAsync(
                    "work-directory",
                    directory,
                    exception.Message,
                    CancellationToken.None);
            }
        }
    }

    private async Task CleanupStoredFrameImagesAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
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
                if (!await DeleteObjectQuietlyAsync(frame.FrameUrl, "frame image", metrics))
                {
                    metrics.FailureCount++;
                }

                frame.FrameUrl = string.Empty;
                metrics.FrameObjectsCleared++;
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupOrphanUploadTemporaryFilesAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
    {
        var rootPath = Path.GetFullPath(_storageProtectionOptions.UploadTempRootPath);
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(rootPath, "upload-*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var fullPath = Path.GetFullPath(file);
                if (!IsInsideDirectory(fullPath, rootPath))
                {
                    logger.LogWarning("Skipped unsafe upload temp cleanup path {Path}.", fullPath);
                    continue;
                }

                if (File.GetLastWriteTimeUtc(fullPath) > cutoff.UtcDateTime)
                {
                    continue;
                }

                File.Delete(fullPath);
                metrics.UploadTemporaryFilesDeleted++;
                logger.LogInformation("Deleted expired upload temp file {Path}.", fullPath);
            }
            catch (Exception exception)
            {
                metrics.FailureCount++;
                metrics.ErrorMessage ??= exception.Message;
                logger.LogWarning(exception, "Failed to delete expired upload temp file {Path}.", file);
                await monitoringAlertService.RecordCleanupFailureAsync(
                    "upload-temp-file",
                    file,
                    exception.Message,
                    CancellationToken.None);
            }
        }
    }

    private async Task CleanupExpiredOriginalMediaAsync(DateTimeOffset now, CleanupMetrics metrics)
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
                if (!await DeleteObjectQuietlyAsync(video.FileUrl, "original video", metrics))
                {
                    metrics.FailureCount++;
                }

                if (!string.IsNullOrWhiteSpace(video.ThumbnailUrl))
                {
                    if (!await DeleteObjectQuietlyAsync(video.ThumbnailUrl, "thumbnail", metrics))
                    {
                        metrics.FailureCount++;
                    }

                    video.ThumbnailUrl = null;
                    metrics.ThumbnailsCleared++;
                }

                video.FileUrl = string.Empty;
                video.RetentionDeleteAt ??= video.CreatedAt.Add(_options.OriginalVideoRetention);
                video.UpdatedAt = now;
                metrics.OriginalVideosCleared++;
            }

            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupExpiredDetailedResultsAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
    {
        await CleanupEvidenceAsync(cutoff, metrics);
        await CleanupSourceMatchesAsync(cutoff, metrics);
        await CleanupProviderPayloadsAsync(cutoff, metrics);
        await CleanupAnalysisPayloadsAsync(cutoff, metrics);
    }

    private async Task CleanupEvidenceAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
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

            metrics.EvidenceRowsDeleted += evidence.Count;
            dbContext.EvidenceItems.RemoveRange(evidence);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupSourceMatchesAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
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

            metrics.SourceMatchRowsDeleted += matches.Count;
            dbContext.SourceMatches.RemoveRange(matches);
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupProviderPayloadsAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
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

            metrics.ProviderPayloadsCleared += requests.Count;
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task CleanupAnalysisPayloadsAsync(DateTimeOffset cutoff, CleanupMetrics metrics)
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

            metrics.AnalysisPayloadsCleared += results.Count;
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

            metrics.SegmentPayloadsCleared += segments.Count;
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task<bool> DeleteObjectQuietlyAsync(string objectKey, string description, CleanupMetrics metrics)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return true;
        }

        try
        {
            await objectStorageService.DeleteAsync(objectKey);
            return true;
        }
        catch (Exception exception)
        {
            metrics.ErrorMessage ??= exception.Message;
            logger.LogWarning(exception, "Failed to delete expired {Description} object {ObjectKey}.", description, objectKey);
            await monitoringAlertService.RecordCleanupFailureAsync(description, objectKey, exception.Message);
            return false;
        }
    }

    private static bool IsInsideDirectory(string path, string rootPath)
    {
        var normalizedRoot = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CleanupMetrics
    {
        public int WorkDirectoriesDeleted { get; set; }

        public int UploadTemporaryFilesDeleted { get; set; }

        public int FrameObjectsCleared { get; set; }

        public int OriginalVideosCleared { get; set; }

        public int ThumbnailsCleared { get; set; }

        public int EvidenceRowsDeleted { get; set; }

        public int SourceMatchRowsDeleted { get; set; }

        public int ProviderPayloadsCleared { get; set; }

        public int AnalysisPayloadsCleared { get; set; }

        public int SegmentPayloadsCleared { get; set; }

        public int FailureCount { get; set; }

        public string? ErrorMessage { get; set; }
    }
}
