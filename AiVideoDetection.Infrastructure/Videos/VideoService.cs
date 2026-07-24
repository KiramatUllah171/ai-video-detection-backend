using System.Security.Cryptography;
using System.Text.Json;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiVideoDetection.Application.Videos.Options;

namespace AiVideoDetection.Infrastructure.Videos;

public class VideoService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IAnalysisJobQueue analysisJobQueue,
    IJobLogService jobLogService,
    IValidator<UploadVideoRequest> uploadValidator,
    IOptions<VideoProcessingOptions> processingOptions,
    ILogger<VideoService> logger) : IVideoService
{
    private readonly VideoProcessingOptions _processingOptions = processingOptions.Value;
    public async Task<ApiResponse<UploadVideoResponse>> UploadAsync(
        UploadVideoRequest request,
        long currentUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await uploadValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Validation failed.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var file = request.File!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var originalName = SanitizeOriginalName(file.FileName);
        var objectKey = CreateObjectKey(currentUserId, extension);
        string? uploadedObjectKey = null;
        var databaseCommitted = false;
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");

        try
        {
            string sha256Hash;
            await using (var tempFile = File.Create(tempFilePath))
            await using (var input = file.OpenReadStream())
            using (var sha256 = SHA256.Create())
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await tempFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                sha256.TransformFinalBlock([], 0, 0);
                sha256Hash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            }

            if (string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Video upload accepted with application/octet-stream for user id {UserId}.", currentUserId);
            }

            await using (var uploadStream = File.OpenRead(tempFilePath))
            {
                uploadedObjectKey = await objectStorageService.UploadAsync(
                    uploadStream,
                    objectKey,
                    file.ContentType,
                    cancellationToken);
            }

            var video = new Video
            {
                UserId = currentUserId,
                OriginalName = originalName,
                FileUrl = uploadedObjectKey,
                ContentType = file.ContentType,
                FileExtension = extension,
                FileSize = file.Length,
                Sha256Hash = sha256Hash,
                Status = VideoStatus.Uploaded
            };

            var job = new AnalysisJob
            {
                Video = video,
                Status = JobStatus.Queued,
                Progress = 0,
                CurrentStep = "Waiting for processing worker",
                RetryCount = 0,
                MaxRetryCount = _processingOptions.UserRetryLimit,
                ScanMode = request.AnalysisMode.ToString(),
                LastActivityAt = DateTimeOffset.UtcNow
            };

            if (dbContext.Database.IsRelational())
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                dbContext.Videos.Add(video);
                dbContext.AnalysisJobs.Add(job);
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                databaseCommitted = true;
            }
            else
            {
                dbContext.Videos.Add(video);
                dbContext.AnalysisJobs.Add(job);
                await dbContext.SaveChangesAsync(cancellationToken);
                databaseCommitted = true;
            }

            var backgroundJobId = analysisJobQueue.EnqueueAnalysisJob(job.Id);
            logger.LogInformation(
                "Enqueued analysis job {AnalysisJobId} as Hangfire job {BackgroundJobId}.",
                job.Id,
                backgroundJobId);

            return ApiResponse<UploadVideoResponse>.SuccessResponse(
                ToUploadResponse(video, job, "Video uploaded and queued for processing."),
                "Video uploaded successfully.");
        }
        catch
        {
            if (!databaseCommitted && !string.IsNullOrWhiteSpace(uploadedObjectKey))
            {
                await objectStorageService.DeleteAsync(uploadedObjectKey, cancellationToken);
            }

            throw;
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    public async Task<ApiResponse<PagedResponse<VideoHistoryItemDto>>> GetHistoryAsync(
        long currentUserId,
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Videos
            .AsNoTracking()
            .Where(video => video.UserId == currentUserId && video.DeletedAt == null && video.Status != VideoStatus.Deleted);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<VideoStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(video => video.Status == parsedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(video => video.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(video => new VideoHistoryItemDto
            {
                VideoId = video.Id,
                OriginalName = video.OriginalName,
                FileSize = video.FileSize,
                ContentType = video.ContentType,
                FileExtension = video.FileExtension,
                Status = video.Status.ToString(),
                CreatedAt = video.CreatedAt,
                LatestJobId = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => (long?)job.Id)
                    .FirstOrDefault(),
                LatestJobStatus = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => job.Status.ToString())
                    .FirstOrDefault(),
                LatestJobProgress = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => (int?)job.Progress)
                    .FirstOrDefault(),
                CurrentStep = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(job => job.CurrentStep)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResponse<VideoHistoryItemDto>>.SuccessResponse(new PagedResponse<VideoHistoryItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    public async Task<ApiResponse<VideoDetailDto>> GetVideoDetailAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .AsNoTracking()
            .Include(existingVideo => existingVideo.AnalysisJobs)
            .Where(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(cancellationToken);

        return video is null
            ? ApiResponse<VideoDetailDto>.ErrorResponse("Video was not found.")
            : ApiResponse<VideoDetailDto>.SuccessResponse(new VideoDetailDto
            {
                VideoId = video.Id,
                OriginalName = video.OriginalName,
                ContentType = video.ContentType,
                FileExtension = video.FileExtension,
                FileSize = video.FileSize,
                DurationSeconds = video.DurationSeconds,
                Status = video.Status.ToString(),
                CreatedAt = video.CreatedAt,
                UpdatedAt = video.UpdatedAt,
                LatestJob = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(MapJob)
                .FirstOrDefault()
            });
    }

    public async Task<ApiResponse<UploadVideoResponse>> ReanalyzeAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .FirstOrDefaultAsync(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted,
                cancellationToken);

        if (video is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Video was not found.");
        }

        var job = new AnalysisJob
        {
            VideoId = video.Id,
            Status = JobStatus.Queued,
            Progress = 0,
            CurrentStep = "Waiting for reanalysis worker",
            RetryCount = 0,
            MaxRetryCount = _processingOptions.UserRetryLimit,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        video.Status = VideoStatus.Queued;
        dbContext.AnalysisJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);

        var backgroundJobId = analysisJobQueue.EnqueueAnalysisJob(job.Id);
        logger.LogInformation(
            "Queued reanalysis job {AnalysisJobId} as Hangfire job {BackgroundJobId} for video {VideoId}.",
            job.Id,
            backgroundJobId,
            video.Id);

        return ApiResponse<UploadVideoResponse>.SuccessResponse(
            ToUploadResponse(video, job, "Video queued for reanalysis."),
            "Video queued for reanalysis.");
    }

    public async Task<ApiResponse<UploadVideoResponse>> RetryAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var video = await dbContext.Videos
            .Include(existingVideo => existingVideo.AnalysisJobs)
            .FirstOrDefaultAsync(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted,
                cancellationToken);

        if (video is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Video was not found.");
        }

        var latestJob = video.AnalysisJobs
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .FirstOrDefault();

        var activeJob = video.AnalysisJobs
            .Where(IsActiveJob)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .FirstOrDefault();
        if (activeJob is not null)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ApiResponse<UploadVideoResponse>.SuccessResponse(
                ToUploadResponse(video, activeJob, "Analysis retry is already queued or processing."),
                "Analysis retry is already queued or processing.");
        }

        if (latestJob is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("No failed analysis job was found for this video.");
        }

        if (latestJob.Status != JobStatus.Failed)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Only failed analyses can be retried from this endpoint.");
        }

        if (latestJob.RetryCount >= latestJob.MaxRetryCount)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Maximum retry count was reached.");
        }

        var job = new AnalysisJob
        {
            VideoId = video.Id,
            Status = JobStatus.Queued,
            Progress = 0,
            CurrentStep = "Waiting for retry worker",
            RetryCount = latestJob.RetryCount + 1,
            MaxRetryCount = latestJob.MaxRetryCount,
            ScanMode = latestJob.ScanMode,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        video.Status = VideoStatus.Queued;
        dbContext.AnalysisJobs.Add(job);
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        var backgroundJobId = analysisJobQueue.EnqueueAnalysisJob(job.Id);
        logger.LogInformation(
            "Queued retry analysis job {AnalysisJobId} as Hangfire job {BackgroundJobId} for video {VideoId} from failed job {FailedJobId}.",
            job.Id,
            backgroundJobId,
            video.Id,
            latestJob.Id);

        return ApiResponse<UploadVideoResponse>.SuccessResponse(
            ToUploadResponse(video, job, "Analysis retry queued."),
            "Analysis retry queued.");
    }

    public async Task<ApiResponse<JobStatusDto>> CancelAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.AnalysisJobs
            .Include(existingJob => existingJob.Video)
            .Include(existingJob => existingJob.Segments)
            .Where(existingJob => existingJob.VideoId == videoId
                && existingJob.Video.UserId == currentUserId
                && existingJob.Video.DeletedAt == null
                && existingJob.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(existingJob => existingJob.CreatedAt)
            .ThenByDescending(existingJob => existingJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("Analysis job was not found.");
        }

        if (job.Status == JobStatus.Completed)
        {
            return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Analysis already completed.");
        }

        if (job.Status is JobStatus.Failed or JobStatus.Cancelled)
        {
            return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Analysis is not running.");
        }

        var now = DateTimeOffset.UtcNow;
        job.CancelRequested = true;
        job.CancelRequestedAt ??= now;
        job.LastActivityAt = now;
        job.Status = JobStatus.CancelRequested;
        job.CurrentStep = "Cancelling analysis";
        foreach (var segment in job.Segments.Where(segment => segment.Status is AnalysisSegmentStatus.Pending or AnalysisSegmentStatus.Preparing or AnalysisSegmentStatus.Ready or AnalysisSegmentStatus.Analyzing))
        {
            segment.Status = AnalysisSegmentStatus.CancelRequested;
            segment.LastActivityAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await jobLogService.LogAsync(job.Id, "CancellationRequested", "Information", "Analysis cancellation was requested by the user.", null, cancellationToken);

        return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Cancellation requested.");
    }

    public async Task<ApiResponse<MetadataResultDto>> GetMetadataAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await dbContext.MetadataResults
            .AsNoTracking()
            .Include(result => result.Video)
            .Where(result => result.VideoId == videoId
                && result.Video.UserId == currentUserId
                && result.Video.DeletedAt == null
                && result.Video.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(cancellationToken);

        return metadata is null
            ? ApiResponse<MetadataResultDto>.ErrorResponse("Video metadata was not found.")
            : ApiResponse<MetadataResultDto>.SuccessResponse(new MetadataResultDto
            {
                VideoId = metadata.VideoId,
                Codec = metadata.Codec,
                AudioCodec = metadata.AudioCodec,
                Fps = metadata.Fps,
                Resolution = metadata.Resolution,
                DurationSeconds = metadata.DurationSeconds,
                Bitrate = metadata.Bitrate,
                Encoder = metadata.Encoder,
                CreationTime = metadata.CreationTime,
                HasMissingMetadata = metadata.HasMissingMetadata,
                WarningsJson = metadata.WarningsJson,
                CreatedAt = metadata.CreatedAt
            });
    }

    public async Task<ApiResponse<IReadOnlyList<VideoFrameDto>>> GetFramesAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var ownsVideo = await dbContext.Videos
            .AsNoTracking()
            .AnyAsync(video => video.Id == videoId
                && video.UserId == currentUserId
                && video.DeletedAt == null
                && video.Status != VideoStatus.Deleted,
                cancellationToken);

        if (!ownsVideo)
        {
            return ApiResponse<IReadOnlyList<VideoFrameDto>>.ErrorResponse("Video frames were not found.");
        }

        var frames = await dbContext.VideoFrames
            .AsNoTracking()
            .Where(frame => frame.VideoId == videoId)
            .OrderBy(frame => frame.FrameIndex)
            .Select(frame => new VideoFrameDto
            {
                Id = frame.Id,
                VideoId = frame.VideoId,
                FrameUrl = frame.FrameUrl,
                TimestampSeconds = frame.TimestampSeconds,
                FrameIndex = frame.FrameIndex,
                Width = frame.Width,
                Height = frame.Height,
                IsKeyframe = frame.IsKeyframe,
                CreatedAt = frame.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<IReadOnlyList<VideoFrameDto>>.SuccessResponse(frames);
    }

    public async Task<ApiResponse<AnalysisResultDto>> GetAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await dbContext.AiResults
            .AsNoTracking()
            .Include(aiResult => aiResult.Video)
            .Include(aiResult => aiResult.ModelVersion)
            .Include(aiResult => aiResult.EvidenceItems)
            .Where(aiResult => aiResult.VideoId == videoId
                && aiResult.Video.UserId == currentUserId
                && aiResult.Video.DeletedAt == null
                && aiResult.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(aiResult => aiResult.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return result is null
            ? ApiResponse<AnalysisResultDto>.ErrorResponse("Analysis result is not available yet.")
            : ApiResponse<AnalysisResultDto>.SuccessResponse(new AnalysisResultDto
            {
                VideoId = result.VideoId,
                AiResultId = result.Id,
                ModelId = SanitizeProviderValue(GetRawString(result.RawModelOutputJson, "model_id") ?? result.ModelVersion?.Version),
                ModelVersion = SanitizeProviderValue(result.ModelVersion?.Version),
                ModelCapability = GetRawString(result.RawModelOutputJson, "model_capability"),
                IsMock = GetRawBool(result.RawModelOutputJson, "is_mock"),
                AiGeneratedProbability = ToPercentage(result.FinalScore),
                LikelyRealProbability = ToPercentage(1m - result.FinalScore),
                ConfidencePercentage = ToPercentage(result.Confidence),
                VisualScore = result.VisualScore,
                MetadataScore = result.MetadataScore,
                TemporalScore = result.TemporalScore,
                FinalScore = result.FinalScore,
                Confidence = result.Confidence,
                Label = result.Label.ToString(),
                Summary = SanitizeProviderText(result.Summary),
                Warnings = GetRawStringArray(result.RawModelOutputJson, "warnings"),
                Provider = SanitizeProviderValue(result.Provider) ?? "Internal",
                ProviderMode = SanitizeProviderValue(result.ProviderMode) ?? "internal",
                FinalDecisionSource = SanitizeProviderValue(result.FinalDecisionSource) ?? "Internal",
                ExternalProviderName = SanitizeProviderValue(result.ExternalProviderName),
                ExternalProviderStatus = result.ExternalProviderStatus,
                ExternalScore = result.ExternalScore,
                ExternalConfidence = result.ExternalConfidence,
                ExternalLabel = result.ExternalLabel,
                FallbackUsed = result.FallbackUsed,
                FallbackReason = SanitizeProviderText(result.FallbackReason),
                ProviderWarnings = GetRawStringArray(result.RawModelOutputJson, "warnings"),
                LocalAnalysisSummary = BuildProviderSummary("Local", result.LocalResultJson),
                ExternalAnalysisSummary = BuildExternalSummary(result),
                HybridDecisionSummary = result.ProviderMode.Equals("hybrid", StringComparison.OrdinalIgnoreCase)
                    ? SanitizeProviderText(result.Summary)
                    : null,
                ProviderRequestedAt = result.ExternalRequestedAt,
                ProviderCompletedAt = result.ExternalCompletedAt,
                ModelDisagreement = GetRawBool(result.RawModelOutputJson, "model_disagreement"),
                StrongFrameEvidence = GetRawBool(result.RawModelOutputJson, "strong_frame_evidence"),
                MinimumRecommendedScore = GetRawDecimal(result.RawModelOutputJson, "minimum_recommended_score"),
                EnsembleStrategy = GetRawString(result.RawModelOutputJson, "ensemble_strategy"),
                ComponentScoresJson = SanitizeProviderText(GetRawJson(result.RawModelOutputJson, "component_scores")),
                CreatedAt = result.CreatedAt,
                EvidenceItems = result.EvidenceItems
                    .OrderByDescending(item => item.Severity)
                    .ThenBy(item => item.Id)
                    .Select(MapEvidence)
                    .ToList()
            });
    }

    public async Task<ApiResponse<IReadOnlyList<EvidenceItemDto>>> GetEvidenceAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await dbContext.AiResults
            .AsNoTracking()
            .Include(aiResult => aiResult.Video)
            .Include(aiResult => aiResult.EvidenceItems)
            .Where(aiResult => aiResult.VideoId == videoId
                && aiResult.Video.UserId == currentUserId
                && aiResult.Video.DeletedAt == null
                && aiResult.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(aiResult => aiResult.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return result is null
            ? ApiResponse<IReadOnlyList<EvidenceItemDto>>.ErrorResponse("Analysis result is not available yet.")
            : ApiResponse<IReadOnlyList<EvidenceItemDto>>.SuccessResponse(result.EvidenceItems
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Id)
                .Select(MapEvidence)
                .ToList());
    }

    public async Task<ApiResponse<bool>> DeleteVideoAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .FirstOrDefaultAsync(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted,
                cancellationToken);

        if (video is null)
        {
            return ApiResponse<bool>.ErrorResponse("Video was not found.");
        }

        video.Status = VideoStatus.Deleted;
        video.DeletedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Video deleted successfully.");
    }

    internal static JobStatusDto MapJob(AnalysisJob job)
    {
        var errorCategory = MapErrorCategory(job.ErrorCode, job.ErrorMessage);
        var userMessage = job.Status == JobStatus.Failed
            ? GetUserMessage(errorCategory)
            : null;

        return new JobStatusDto
        {
            JobId = job.Id,
            VideoId = job.VideoId,
            Status = job.Status.ToString(),
            Progress = job.Progress,
            CurrentStep = job.CurrentStep,
            ScanMode = job.ScanMode,
            CompletedSegments = job.CompletedSegments,
            TotalSegments = job.TotalSegments,
            AnalyzedCoverageSeconds = job.AnalyzedCoverageSeconds,
            TotalDurationSeconds = job.TotalDurationSeconds,
            LastActivityAt = job.LastActivityAt,
            ErrorMessage = userMessage,
            ErrorCode = job.Status == JobStatus.Failed ? errorCategory : null,
            UserMessage = userMessage,
            CanRetry = job.Status == JobStatus.Failed && job.RetryCount < job.MaxRetryCount,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            FailedStage = job.Status == JobStatus.Failed ? GetFailedStage(job.CurrentStep) : null,
            NextRecommendedAction = job.Status == JobStatus.Failed
                ? job.RetryCount < job.MaxRetryCount
                    ? "RetryAnalysis"
                    : "UploadAnotherVideo"
                : null,
            TechnicalReferenceId = job.Status == JobStatus.Failed ? $"JOB-{job.Id}" : null,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            LastUpdatedAt = job.UpdatedAt
        };
    }

    private static UploadVideoResponse ToUploadResponse(Video video, AnalysisJob job, string message)
    {
        return new UploadVideoResponse
        {
            VideoId = video.Id,
            JobId = job.Id,
            Status = video.Status.ToString(),
            JobStatus = job.Status.ToString(),
            OriginalName = video.OriginalName,
            FileSize = video.FileSize,
            ContentType = video.ContentType ?? string.Empty,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            Message = message
        };
    }

    private static bool IsActiveJob(AnalysisJob job)
    {
        return job.Status is JobStatus.Queued or JobStatus.Preparing or JobStatus.Processing or JobStatus.Retrying or JobStatus.Finalizing or JobStatus.CancelRequested;
    }

    private static string MapErrorCategory(string? errorCode, string? errorMessage)
    {
        var normalizedCode = (errorCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedMessage = (errorMessage ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedCode.Contains("AUTH") || normalizedMessage.Contains("HTTP 401") || normalizedMessage.Contains("UNAUTHORIZED"))
        {
            return "ProviderAuthenticationFailed";
        }

        if (normalizedCode.Contains("RATE") || normalizedMessage.Contains("HTTP 429") || normalizedMessage.Contains("RATE LIMIT"))
        {
            return "ProviderRateLimited";
        }

        if (normalizedCode.Contains("TIMEOUT") || normalizedMessage.Contains("TIMED OUT"))
        {
            return "ProviderTimeout";
        }

        if (normalizedCode.Contains("BITMIND") || normalizedCode.Contains("PROVIDER") || normalizedCode.Contains("AI_SERVICE_UNAVAILABLE") || normalizedCode.Contains("AI_VIDEO_SERVICE_UNAVAILABLE"))
        {
            return "ProviderUnavailable";
        }

        if (normalizedCode.Contains("TOO_LARGE") || normalizedMessage.Contains("UPLOAD LIMIT"))
        {
            return "VideoTooLarge";
        }

        if (normalizedCode.Contains("COMPRESSION"))
        {
            return "CompressionFailed";
        }

        if (normalizedCode.Contains("INVALID") || normalizedCode.Contains("FFPROBE") || normalizedCode.Contains("METADATA"))
        {
            return "InvalidVideo";
        }

        if (normalizedCode.Contains("NETWORK") || normalizedCode.Contains("HTTP_REQUEST"))
        {
            return "NetworkFailure";
        }

        if (normalizedCode.Contains("PROCESS") || normalizedCode.Contains("FFMPEG") || normalizedCode.Contains("AI_ANALYSIS"))
        {
            return "ProcessingFailed";
        }

        return "UnknownFailure";
    }

    private static string GetUserMessage(string errorCategory)
    {
        return errorCategory switch
        {
            "ProviderAuthenticationFailed" => "The external analysis service is temporarily unavailable. Please try again later.",
            "ProviderRateLimited" => "The analysis service is currently busy. Please wait a moment and retry.",
            "ProviderTimeout" => "The external analysis service took too long to respond. Please retry.",
            "ProviderUnavailable" => "External video analysis is temporarily unavailable.",
            "VideoTooLarge" => "The video could not be prepared for external analysis.",
            "CompressionFailed" => "We could not prepare this video for analysis. Please retry or upload a different format.",
            "InvalidVideo" => "This file could not be processed as a valid video.",
            "NetworkFailure" => "A temporary connection problem interrupted the analysis.",
            "ProcessingFailed" => "We could not complete the analysis. Please retry.",
            _ => "Something went wrong while processing this video."
        };
    }

    private static string? GetFailedStage(string? currentStep)
    {
        if (string.IsNullOrWhiteSpace(currentStep))
        {
            return null;
        }

        return currentStep.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase)
            ? "Failed"
            : currentStep;
    }

    private static EvidenceItemDto MapEvidence(EvidenceItem item)
    {
        return new EvidenceItemDto
        {
            Id = item.Id,
            Type = item.Type.ToString(),
            Severity = item.Severity.ToString(),
            Title = SanitizeProviderText(item.Title) ?? item.Title,
            Description = SanitizeProviderText(item.Description) ?? item.Description,
            ScoreImpact = item.ScoreImpact,
            TimestampSeconds = item.TimestampSeconds,
            VideoFrameId = item.VideoFrameId
        };
    }

    private static decimal ToPercentage(decimal value)
    {
        return Math.Round(Math.Clamp(value, 0m, 1m) * 100m, 2);
    }

    private static string? GetRawString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool GetRawBool(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                && value.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetRawStringArray(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => SanitizeProviderText(item.GetString()!)!)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static decimal? GetRawDecimal(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetRawJson(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.GetRawText()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildProviderSummary(string providerName, string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return null;
        }

        var score = GetRawDecimal(resultJson, "overall_ai_score");
        var confidence = GetRawDecimal(resultJson, "overall_confidence");
        var label = GetRawString(resultJson, "label_hint");
        return $"{SanitizeProviderValue(providerName)}: {label ?? "analysis completed"}"
            + (score is null ? string.Empty : $" ({ToPercentage(score.Value)}% AI)")
            + (confidence is null ? string.Empty : $", {ToPercentage(confidence.Value)}% confidence");
    }

    private static string? BuildExternalSummary(AiResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ExternalProviderName))
        {
            return null;
        }

        return $"{SanitizeProviderValue(result.ExternalProviderName)}: {result.ExternalProviderStatus ?? "Unknown"}"
            + (result.ExternalLabel is null ? string.Empty : $", {result.ExternalLabel}")
            + (result.ExternalScore is null ? string.Empty : $" ({ToPercentage(result.ExternalScore.Value)}% AI)")
            + (result.ExternalConfidence is null ? string.Empty : $", {ToPercentage(result.ExternalConfidence.Value)}% confidence");
    }

    private static string? SanitizeProviderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Trim().Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalized.Contains("bitmind", StringComparison.OrdinalIgnoreCase))
        {
            return "External";
        }

        if (string.Equals(normalized, "FallbackLocal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return "Internal";
        }

        return SanitizeProviderText(value);
    }

    private static string? SanitizeProviderText(string? value)
    {
        return value?
            .Replace("bitmind-oracle-v1-sn34", "external-verification-model", StringComparison.OrdinalIgnoreCase)
            .Replace("bitmind-subnet-34", "external-verification-model", StringComparison.OrdinalIgnoreCase)
            .Replace("External BitMind verification", "External verification", StringComparison.OrdinalIgnoreCase)
            .Replace("BitMind", "external verification", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateObjectKey(long userId, string extension)
    {
        var now = DateTimeOffset.UtcNow;
        return $"videos/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
    }

    private static string SanitizeOriginalName(string originalName)
    {
        var fileName = Path.GetFileName(originalName);
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName.Length <= 500 ? fileName : fileName[..500];
    }
}
