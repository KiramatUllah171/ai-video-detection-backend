using System.Text.Json;
using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class VideoProcessingService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IMetadataExtractionService metadataExtractionService,
    IFrameExtractionService frameExtractionService,
    IAiInferenceClient aiInferenceClient,
    IFinalScoringService finalScoringService,
    IEvidenceGenerationService evidenceGenerationService,
    IJobLogService jobLogService,
    IOptions<VideoProcessingOptions> options,
    ILogger<VideoProcessingService> logger) : IVideoProcessingService
{
    private const string CompletedStep = "AI analysis completed";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly VideoProcessingOptions _options = options.Value;

    public async Task ProcessAnalysisJobAsync(long jobId, CancellationToken cancellationToken = default)
    {
        var job = await dbContext.AnalysisJobs
            .Include(existingJob => existingJob.Video)
            .FirstOrDefaultAsync(existingJob => existingJob.Id == jobId, cancellationToken);

        if (job is null)
        {
            logger.LogWarning("Analysis job {JobId} was not found.", jobId);
            return;
        }

        if (job.Status == JobStatus.Completed)
        {
            logger.LogInformation("Analysis job {JobId} is already completed. Skipping.", jobId);
            return;
        }

        if (job.Status == JobStatus.Processing
            && job.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-10))
        {
            logger.LogInformation("Analysis job {JobId} already appears active. Skipping duplicate invocation.", jobId);
            return;
        }

        if (job.Video is null || job.Video.DeletedAt is not null || job.Video.Status == VideoStatus.Deleted)
        {
            await MarkFailedAsync(job, "PreparingVideo", "VIDEO_NOT_AVAILABLE", "Video is not available for processing.", cancellationToken);
            return;
        }

        if (job.Status == JobStatus.Failed || job.Status == JobStatus.Retrying)
        {
            if (job.RetryCount >= job.MaxRetryCount)
            {
                await MarkFailedAsync(job, "PreparingVideo", "MAX_RETRIES_EXCEEDED", "Maximum processing retry count was reached.", cancellationToken);
                return;
            }

            job.RetryCount++;
        }

        var workDirectory = Path.GetFullPath(Path.Combine(_options.WorkingRootPath, job.Id.ToString()));

        try
        {
            await StartProcessingAsync(job, cancellationToken);

            Directory.CreateDirectory(workDirectory);
            var sourcePath = Path.Combine(workDirectory, $"source{job.Video.FileExtension ?? ".video"}");

            await UpdateProgressAsync(job, 10, "Downloading source video", cancellationToken);
            await objectStorageService.DownloadToAsync(job.Video.FileUrl, sourcePath, cancellationToken);
            await jobLogService.LogAsync(job.Id, "PreparingVideo", "Information", "Source video prepared for FFmpeg processing.", null, cancellationToken);

            var input = new VideoProcessingInput(job.Id, job.VideoId, sourcePath, workDirectory, job.Video.DurationSeconds);

            await UpdateProgressAsync(job, 20, "Extracting video metadata", cancellationToken);
            var metadata = await metadataExtractionService.ExtractMetadataAsync(input, cancellationToken);
            await SaveMetadataAsync(job.Video, metadata, cancellationToken);
            await jobLogService.LogAsync(job.Id, "MetadataExtraction", "Information", "Video metadata extracted.", new
            {
                metadata.DurationSeconds,
                metadata.Codec,
                metadata.AudioCodec,
                metadata.Resolution,
                warnings = metadata.Warnings
            }, cancellationToken);

            input = input with { DurationSeconds = metadata.DurationSeconds ?? job.Video.DurationSeconds };

            await UpdateProgressAsync(job, 40, "Generating video thumbnail", cancellationToken);
            var thumbnail = await frameExtractionService.GenerateThumbnailAsync(input, cancellationToken);
            await UploadThumbnailAsync(job.Video, thumbnail, cancellationToken);
            await jobLogService.LogAsync(job.Id, "ThumbnailGeneration", "Information", "Video thumbnail generated.", new
            {
                thumbnail.TimestampSeconds
            }, cancellationToken);

            await UpdateProgressAsync(job, 65, "Extracting representative video frames", cancellationToken);
            var frames = await frameExtractionService.ExtractFramesAsync(input, cancellationToken);
            await UploadFramesAsync(job.VideoId, frames, metadata.Resolution, cancellationToken);
            await jobLogService.LogAsync(job.Id, "FrameExtraction", "Information", "Representative video frames extracted.", new
            {
                frameCount = frames.Count
            }, cancellationToken);

            await UpdateProgressAsync(job, 80, "Running AI frame analysis", cancellationToken);
            await jobLogService.LogAsync(job.Id, "AiAnalysisStarted", "Information", "AI frame analysis started.", null, cancellationToken);
            var savedFrames = await dbContext.VideoFrames
                .Where(frame => frame.VideoId == job.VideoId)
                .OrderBy(frame => frame.FrameIndex)
                .ToListAsync(cancellationToken);
            var aiResponse = await AnalyzeFramesAsync(job, savedFrames, cancellationToken);
            await jobLogService.LogAsync(job.Id, "AiAnalysisCompleted", "Information", "AI frame analysis completed.", new
            {
                aiResponse.ModelVersion,
                aiResponse.OverallAiScore,
                aiResponse.OverallConfidence
            }, cancellationToken);

            var metadataWarnings = ParseMetadataWarnings(await dbContext.MetadataResults
                .AsNoTracking()
                .Where(result => result.VideoId == job.VideoId)
                .Select(result => result.WarningsJson)
                .FirstOrDefaultAsync(cancellationToken));
            var scoringResult = finalScoringService.Calculate(new FinalScoringInput(
                aiResponse.OverallAiScore,
                aiResponse.OverallConfidence,
                metadataWarnings,
                savedFrames.Count,
                TemporalScore: null,
                aiResponse.LabelHint));
            await jobLogService.LogAsync(job.Id, "ScoringCompleted", "Information", "Final scoring completed.", new
            {
                scoringResult.FinalScore,
                scoringResult.Confidence,
                label = scoringResult.Label.ToString()
            }, cancellationToken);

            var aiResult = await SaveAiResultAsync(job.VideoId, aiResponse, scoringResult, cancellationToken);
            var evidenceItems = evidenceGenerationService.GenerateEvidence(new EvidenceGenerationInput(
                aiResponse.ModelVersion,
                aiResponse.Frames,
                savedFrames.ToDictionary(frame => frame.Id, frame => frame.Id),
                metadataWarnings,
                scoringResult));
            await ReplaceEvidenceItemsAsync(aiResult, evidenceItems, cancellationToken);
            await jobLogService.LogAsync(job.Id, "EvidenceGenerated", "Information", "Evidence items generated.", new
            {
                evidenceCount = evidenceItems.Count
            }, cancellationToken);

            job.Status = JobStatus.Completed;
            job.Progress = 100;
            job.CurrentStep = CompletedStep;
            job.ErrorCode = null;
            job.ErrorMessage = null;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.Video.Status = VideoStatus.Completed;
            await dbContext.SaveChangesAsync(cancellationToken);

            await jobLogService.LogAsync(job.Id, "Completed", "Information", CompletedStep, null, cancellationToken);
        }
        catch (ProcessingException exception)
        {
            var errorCode = MapProcessingErrorCode(exception.ErrorCode, job.CurrentStep);
            logger.LogError(exception, "Analysis job {JobId} failed with {ErrorCode}.", job.Id, errorCode);
            await MarkFailedAsync(job, "Failed", errorCode, exception.SafeMessage, cancellationToken);
            throw;
        }
        catch (AiServiceException exception)
        {
            logger.LogError(exception, "AI analysis failed for analysis job {JobId} with {ErrorCode}.", job.Id, exception.ErrorCode);
            await MarkFailedAsync(job, "AiAnalysisFailed", exception.ErrorCode, exception.SafeMessage, cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Analysis job {JobId} failed with an unexpected processing error.", job.Id);
            await MarkFailedAsync(job, "Failed", "UNKNOWN_PROCESSING_ERROR", "Video processing failed unexpectedly.", cancellationToken);
            throw;
        }
        finally
        {
            TryDeleteDirectory(workDirectory);
        }
    }

    private async Task<AiAnalyzeFramesResponse> AnalyzeFramesAsync(
        AnalysisJob job,
        IReadOnlyList<VideoFrame> frames,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            throw new AiServiceException("AI_ANALYSIS_FAILED", "AI analysis could not be completed because no frames were available.");
        }

        var request = new AiAnalyzeFramesRequest(
            job.VideoId,
            job.Id,
            frames.Select(frame => new AiAnalyzeFrameItem(
                frame.Id,
                frame.FrameUrl,
                frame.FrameIndex,
                frame.TimestampSeconds)).ToList());

        return await aiInferenceClient.AnalyzeFramesAsync(request, cancellationToken);
    }

    private async Task<AiResult> SaveAiResultAsync(
        long videoId,
        AiAnalyzeFramesResponse aiResponse,
        FinalScoringResult scoringResult,
        CancellationToken cancellationToken)
    {
        var modelVersion = await GetOrCreateModelVersionAsync(aiResponse.ModelVersion, cancellationToken);
        var aiResult = await dbContext.AiResults
            .Include(result => result.EvidenceItems)
            .Where(result => result.VideoId == videoId && result.ModelVersionId == modelVersion.Id)
            .OrderByDescending(result => result.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        aiResult ??= new AiResult
        {
            VideoId = videoId,
            ModelVersionId = modelVersion.Id
        };

        aiResult.VisualScore = scoringResult.VisualScore;
        aiResult.MetadataScore = scoringResult.MetadataScore;
        aiResult.TemporalScore = scoringResult.TemporalScore;
        aiResult.FinalScore = scoringResult.FinalScore;
        aiResult.Confidence = scoringResult.Confidence;
        aiResult.Label = scoringResult.Label;
        aiResult.Summary = scoringResult.Summary;
        aiResult.RawModelOutputJson = string.IsNullOrWhiteSpace(aiResponse.RawJson)
            ? JsonSerializer.Serialize(aiResponse, SerializerOptions)
            : aiResponse.RawJson;

        if (aiResult.Id == 0)
        {
            dbContext.AiResults.Add(aiResult);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return aiResult;
    }

    private async Task<ModelVersion> GetOrCreateModelVersionAsync(string version, CancellationToken cancellationToken)
    {
        var modelVersion = await dbContext.ModelVersions
            .FirstOrDefaultAsync(model => model.Version == version, cancellationToken);

        if (modelVersion is not null)
        {
            return modelVersion;
        }

        modelVersion = new ModelVersion
        {
            Name = version.StartsWith("mock", StringComparison.OrdinalIgnoreCase) ? "Mock Video AI" : version,
            Version = version,
            Description = version.StartsWith("mock", StringComparison.OrdinalIgnoreCase)
                ? "Mock deterministic AI scoring model for pipeline integration."
                : "AI model version reported by the external AI inference service.",
            IsActive = true
        };
        dbContext.ModelVersions.Add(modelVersion);
        await dbContext.SaveChangesAsync(cancellationToken);
        return modelVersion;
    }

    private async Task ReplaceEvidenceItemsAsync(
        AiResult aiResult,
        IReadOnlyList<CreateEvidenceItemDto> evidenceItems,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.EvidenceItems
            .Where(item => item.AiResultId == aiResult.Id)
            .ToListAsync(cancellationToken);
        dbContext.EvidenceItems.RemoveRange(existing);

        foreach (var item in evidenceItems)
        {
            dbContext.EvidenceItems.Add(new EvidenceItem
            {
                AiResultId = aiResult.Id,
                VideoFrameId = item.VideoFrameId,
                Type = item.Type,
                Severity = item.Severity,
                Title = item.Title,
                Description = item.Description,
                ScoreImpact = item.ScoreImpact,
                TimestampSeconds = item.TimestampSeconds
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static IReadOnlyList<string> ParseMetadataWarnings(string? warningsJson)
    {
        if (string.IsNullOrWhiteSpace(warningsJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<IReadOnlyList<string>>(warningsJson, SerializerOptions) ?? [];
        }
        catch
        {
            return ["unreadable_metadata"];
        }
    }

    private async Task StartProcessingAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        job.Status = JobStatus.Processing;
        job.Progress = 5;
        job.CurrentStep = "Preparing video for processing";
        job.StartedAt ??= DateTimeOffset.UtcNow;
        job.CompletedAt = null;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.Video.Status = VideoStatus.Processing;

        await dbContext.SaveChangesAsync(cancellationToken);
        await jobLogService.LogAsync(job.Id, "PreparingVideo", "Information", "Analysis job started.", null, cancellationToken);
    }

    private async Task UpdateProgressAsync(
        AnalysisJob job,
        int progress,
        string currentStep,
        CancellationToken cancellationToken)
    {
        job.Progress = progress;
        job.CurrentStep = currentStep;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SaveMetadataAsync(
        Video video,
        ExtractedMetadataResult metadata,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.MetadataResults
            .FirstOrDefaultAsync(result => result.VideoId == video.Id, cancellationToken);

        existing ??= new MetadataResult { VideoId = video.Id };

        existing.Codec = metadata.Codec;
        existing.AudioCodec = metadata.AudioCodec;
        existing.Fps = metadata.Fps;
        existing.Resolution = metadata.Resolution;
        existing.DurationSeconds = metadata.DurationSeconds;
        existing.Bitrate = metadata.Bitrate;
        existing.Encoder = metadata.Encoder;
        existing.CreationTime = metadata.CreationTime;
        existing.HasMissingMetadata = metadata.HasMissingMetadata;
        existing.WarningsJson = JsonSerializer.Serialize(metadata.Warnings, SerializerOptions);
        existing.RawJson = string.IsNullOrWhiteSpace(metadata.RawJson) ? "{}" : metadata.RawJson;

        if (existing.Id == 0)
        {
            dbContext.MetadataResults.Add(existing);
        }

        video.DurationSeconds = metadata.DurationSeconds;
        video.FormatName = Truncate(metadata.FormatName, 100);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UploadThumbnailAsync(
        Video video,
        ThumbnailResult thumbnail,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(thumbnail.FilePath).TrimStart('.').ToLowerInvariant();
        var objectKey = $"thumbnails/{video.Id}/thumbnail.{extension}";

        await using var stream = File.OpenRead(thumbnail.FilePath);
        video.ThumbnailUrl = await objectStorageService.UploadAsync(stream, objectKey, GetImageContentType(extension), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task UploadFramesAsync(
        long videoId,
        IReadOnlyList<ExtractedFrameResult> frames,
        string? resolution,
        CancellationToken cancellationToken)
    {
        var (width, height) = ParseResolution(resolution);

        foreach (var frame in frames)
        {
            var extension = Path.GetExtension(frame.FilePath).TrimStart('.').ToLowerInvariant();
            var objectKey = $"frames/{videoId}/frame_{frame.FrameIndex:000000}.{extension}";

            await using var stream = File.OpenRead(frame.FilePath);
            var frameUrl = await objectStorageService.UploadAsync(stream, objectKey, GetImageContentType(extension), cancellationToken);

            var existing = await dbContext.VideoFrames
                .FirstOrDefaultAsync(
                    existingFrame => existingFrame.VideoId == videoId && existingFrame.FrameIndex == frame.FrameIndex,
                    cancellationToken);

            existing ??= new VideoFrame
            {
                VideoId = videoId,
                FrameIndex = frame.FrameIndex
            };

            existing.FrameUrl = frameUrl;
            existing.TimestampSeconds = frame.TimestampSeconds;
            existing.Width = frame.Width ?? width;
            existing.Height = frame.Height ?? height;
            existing.IsKeyframe = frame.IsKeyframe;

            if (existing.Id == 0)
            {
                dbContext.VideoFrames.Add(existing);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailedAsync(
        AnalysisJob job,
        string stepName,
        string errorCode,
        string safeMessage,
        CancellationToken cancellationToken)
    {
        job.Status = JobStatus.Failed;
        job.Progress = Math.Clamp(job.Progress, 0, 99);
        job.CurrentStep = $"Failed: {safeMessage}";
        job.ErrorCode = errorCode;
        job.ErrorMessage = safeMessage;
        job.CompletedAt ??= DateTimeOffset.UtcNow;

        if (job.Video is not null)
        {
            job.Video.Status = VideoStatus.Failed;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await jobLogService.LogAsync(job.Id, stepName, "Error", safeMessage, new { errorCode }, cancellationToken);
    }

    private static string MapProcessingErrorCode(string errorCode, string? currentStep)
    {
        if (errorCode == "PROCESS_NOT_FOUND")
        {
            return currentStep?.Contains("metadata", StringComparison.OrdinalIgnoreCase) == true
                ? "FFPROBE_NOT_FOUND"
                : "FFMPEG_NOT_FOUND";
        }

        return errorCode;
    }

    private static string GetImageContentType(string extension)
    {
        return extension is "png" ? "image/png" : "image/jpeg";
    }

    private static (int? Width, int? Height) ParseResolution(string? resolution)
    {
        if (string.IsNullOrWhiteSpace(resolution))
        {
            return (null, null);
        }

        var parts = resolution.Split('x', 2);
        return parts.Length == 2
            && int.TryParse(parts[0], out var width)
            && int.TryParse(parts[1], out var height)
            ? (width, height)
            : (null, null);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        return value is null || value.Length <= maxLength ? value : value[..maxLength];
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Cleanup is best effort; job status should not change because temp deletion failed.
        }
    }
}
