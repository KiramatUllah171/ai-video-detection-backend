using System.Text.Json;
using System.Globalization;
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
    IProcessRunner processRunner,
    IAiInferenceClient aiInferenceClient,
    IFinalScoringService finalScoringService,
    IEvidenceGenerationService evidenceGenerationService,
    IFrameHashService frameHashService,
    IInternalVideoMatchingService internalVideoMatchingService,
    IJobLogService jobLogService,
    IOptions<VideoProcessingOptions> options,
    IOptions<InternalMatchingOptions> internalMatchingOptions,
    IOptions<AiServiceOptions> aiServiceOptions,
    ILogger<VideoProcessingService> logger) : IVideoProcessingService
{
    private const string CompletedStep = "Analysis completed";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly VideoProcessingOptions _options = options.Value;
    private readonly InternalMatchingOptions _internalMatchingOptions = internalMatchingOptions.Value;
    private readonly AiServiceOptions _aiServiceOptions = aiServiceOptions.Value;

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

        if (job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
        {
            logger.LogInformation("Analysis job {JobId} is terminal ({Status}). Skipping.", jobId, job.Status);
            return;
        }

        if (job.Status == JobStatus.Paused)
        {
            logger.LogInformation("Analysis job {JobId} is paused. Skipping until the user resumes it.", jobId);
            return;
        }

        if (job.Status == JobStatus.Processing
            && job.UpdatedAt > DateTimeOffset.UtcNow.AddMinutes(-10))
        {
            logger.LogInformation("Analysis job {JobId} already appears active. Skipping duplicate invocation.", jobId);
            return;
        }

        if (job.Video is null
            || job.Video.DeletedAt is not null
            || job.Video.Status == VideoStatus.Deleted
            || string.IsNullOrWhiteSpace(job.Video.FileUrl))
        {
            await MarkFailedAsync(job, "PreparingVideo", "VIDEO_NOT_AVAILABLE", "Video is not available for processing.", cancellationToken);
            return;
        }

        var workDirectory = Path.GetFullPath(Path.Combine(_options.WorkingRootPath, job.Id.ToString()));

        try
        {
            await CheckForPauseOrCancellationAsync(job, "Before starting analysis", cancellationToken);
            await StartProcessingAsync(job, cancellationToken);

            Directory.CreateDirectory(workDirectory);
            var sourcePath = Path.Combine(workDirectory, $"source{job.Video.FileExtension ?? ".video"}");

            await CheckForPauseOrCancellationAsync(job, "Before preparing source video", cancellationToken);
            await UpdateProgressAsync(job, 10, "Preparing video", cancellationToken);
            await objectStorageService.DownloadToAsync(job.Video.FileUrl, sourcePath, cancellationToken);
            await jobLogService.LogAsync(job.Id, "PreparingVideo", "Information", "Source video prepared for processing.", null, cancellationToken);

            var input = new VideoProcessingInput(job.Id, job.VideoId, sourcePath, workDirectory, job.Video.DurationSeconds);

            await CheckForPauseOrCancellationAsync(job, "Before metadata extraction", cancellationToken);
            await UpdateProgressAsync(job, 18, "Preparing video", cancellationToken);
            var metadata = await metadataExtractionService.ExtractMetadataAsync(input, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "After metadata extraction", cancellationToken);
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

            ValidateMetadata(metadata);

            await CheckForPauseOrCancellationAsync(job, "Before thumbnail generation", cancellationToken);
            await UpdateProgressAsync(job, 22, "Preparing video", cancellationToken);
            var thumbnail = await frameExtractionService.GenerateThumbnailAsync(input, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "After thumbnail generation", cancellationToken);
            await UploadThumbnailAsync(job.Video, thumbnail, cancellationToken);
            await jobLogService.LogAsync(job.Id, "ThumbnailGeneration", "Information", "Video thumbnail generated.", new
            {
                thumbnail.TimestampSeconds
            }, cancellationToken);

            await CheckForPauseOrCancellationAsync(job, "Before frame extraction", cancellationToken);
            var frames = await frameExtractionService.ExtractFramesAsync(input, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "After frame extraction", cancellationToken);
            await UploadFramesAsync(job.VideoId, frames, metadata.Resolution, cancellationToken);

            await CheckForPauseOrCancellationAsync(job, "Before scan segment planning", cancellationToken);
            var segments = await GetOrCreateSegmentsAsync(job, input.DurationSeconds!.Value, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "After scan segment planning", cancellationToken);
            await jobLogService.LogAsync(job.Id, "SmartScanPlanned", "Information", "Smart Scan timeline plan created.", new
            {
                segmentCount = segments.Count,
                durationSeconds = input.DurationSeconds,
                scanMode = job.ScanMode
            }, cancellationToken);

            await ProcessSegmentsAsync(job, segments, sourcePath, workDirectory, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "After scan segments", cancellationToken);
            var aiResponse = BuildAggregateResponse(job, segments);

            await CheckForPauseOrCancellationAsync(job, "Before final score calculation", cancellationToken);
            var metadataWarnings = ParseMetadataWarnings(await dbContext.MetadataResults
                .AsNoTracking()
                .Where(result => result.VideoId == job.VideoId)
                .Select(result => result.WarningsJson)
                .FirstOrDefaultAsync(cancellationToken));
            var allowMinimumRecommendation = !(aiResponse.ModelDisagreement
                && aiResponse.VideoComponentScore is < 0.50m
                && aiResponse.MinimumRecommendedScore is null);
            var visualScore = aiResponse.MinimumRecommendedScore is null || !allowMinimumRecommendation
                ? aiResponse.OverallAiScore
                : Math.Max(aiResponse.OverallAiScore, aiResponse.MinimumRecommendedScore.Value);
            var scoringResult = IsPureBitMindResult(aiResponse)
                ? CreateBitMindScoringResult(aiResponse)
                : finalScoringService.Calculate(new FinalScoringInput(
                    visualScore,
                    aiResponse.OverallConfidence,
                    metadataWarnings,
                    segments.Count,
                    aiResponse.VideoComponentScore,
                    aiResponse.LabelHint,
                    aiResponse.ModelDisagreement,
                    aiResponse.VideoComponentScore,
                    aiResponse.FrameRawScore,
                    aiResponse.FrameCalibratedScore ?? aiResponse.FrameComponentScore,
                    aiResponse.StrongFrameEvidence,
                    null,
                    null,
                    aiResponse.FrameReliabilityScore));
            await CheckForPauseOrCancellationAsync(job, "After final score calculation", cancellationToken);
            await UpdateProgressAsync(job, 92, "Generating final result", cancellationToken);
            await jobLogService.LogAsync(job.Id, "ScoringCompleted", "Information", "Final scoring completed.", new
            {
                scoringResult.FinalScore,
                scoringResult.Confidence,
                label = scoringResult.Label.ToString()
            }, cancellationToken);

            await CheckForPauseOrCancellationAsync(job, "Before saving AI result", cancellationToken);
            var aiResult = await SaveAiResultAsync(job.VideoId, aiResponse, scoringResult, cancellationToken);
            await UpdateProgressAsync(job, 96, "Generating final result", cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "Before evidence generation", cancellationToken);
            var evidenceItems = evidenceGenerationService.GenerateEvidence(new EvidenceGenerationInput(
                aiResponse.ModelVersion,
                aiResponse.Frames,
                new Dictionary<long, long>(),
                metadataWarnings,
                scoringResult,
                aiResponse.ModelDisagreement,
                aiResponse.StrongFrameEvidence));
            await CheckForPauseOrCancellationAsync(job, "After evidence generation", cancellationToken);
            await ReplaceEvidenceItemsAsync(aiResult, evidenceItems, cancellationToken);
            await jobLogService.LogAsync(job.Id, "EvidenceGenerated", "Information", "Evidence items generated.", new
            {
                evidenceCount = evidenceItems.Count
            }, cancellationToken);

            await RunInternalMatchingAsync(job, cancellationToken);

            await CheckForPauseOrCancellationAsync(job, "Before marking completed", cancellationToken);
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
        catch (AnalysisPausedException exception)
        {
            logger.LogInformation(exception, "Analysis job {JobId} paused at checkpoint {Checkpoint}.", job.Id, job.LastCheckpoint);
            return;
        }
        catch (ProcessingException exception)
        {
            if (exception.ErrorCode == "CANCELLED")
            {
                await MarkCancelledAsync(job, cancellationToken);
                return;
            }

            var errorCode = MapProcessingErrorCode(exception.ErrorCode, job.CurrentStep);
            logger.LogError(exception, "Analysis job {JobId} failed with {ErrorCode}.", job.Id, errorCode);
            await MarkFailedAsync(job, "Failed", errorCode, exception.SafeMessage, cancellationToken);
            throw;
        }
        catch (AiServiceException exception)
        {
            logger.LogError(exception, "AI analysis failed for analysis job {JobId} with {ErrorCode}.", job.Id, exception.ErrorCode);
            if (exception.ErrorCode == "REAL_MODEL_NOT_CONFIGURED")
            {
                await MarkFailedAsync(
                    job,
                    "AiAnalysisFailed",
                    exception.ErrorCode,
                    "AI detection model is not configured. Configure a model or enable mock mode for development.",
                    cancellationToken);
                job.CurrentStep = "AI model is not configured";
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            else
            {
                await MarkFailedAsync(job, "AiAnalysisFailed", exception.ErrorCode, exception.SafeMessage, cancellationToken);
            }
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

    private async Task<IReadOnlyList<AnalysisSegment>> GetOrCreateSegmentsAsync(
        AnalysisJob job,
        decimal durationSeconds,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.AnalysisSegments
            .Where(segment => segment.AnalysisJobId == job.Id)
            .OrderBy(segment => segment.SegmentIndex)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            return existing;
        }

        var detailed = string.Equals(job.ScanMode, "Detailed", StringComparison.OrdinalIgnoreCase);
        var tiers = detailed ? _options.DetailedScanTiers : _options.SmartScanTiers;
        var targetCount = tiers
            .OrderBy(tier => tier.MaxDurationSeconds)
            .FirstOrDefault(tier => durationSeconds <= tier.MaxDurationSeconds)?.SegmentCount ?? _options.MaxSegmentCount;
        targetCount = Math.Clamp(targetCount, 1, _options.MaxSegmentCount);
        var clipDuration = Math.Min(_options.SmartScanClipDurationSeconds, durationSeconds);
        var segments = SelectSegmentStarts(durationSeconds, clipDuration, targetCount)
            .Select((start, index) =>
            {
                var end = Math.Min(durationSeconds, start + clipDuration);
                return new AnalysisSegment
                {
                    AnalysisJobId = job.Id,
                    VideoId = job.VideoId,
                    SegmentIndex = index + 1,
                    StartTime = Math.Round(start, 3),
                    EndTime = Math.Round(end, 3),
                    Duration = Math.Round(end - start, 3),
                    Status = AnalysisSegmentStatus.Pending
                };
            })
            .ToList();

        dbContext.AnalysisSegments.AddRange(segments);
        job.TotalSegments = segments.Count;
        job.CompletedSegments = 0;
        job.TotalDurationSeconds = durationSeconds;
        job.AnalyzedCoverageSeconds = 0;
        job.ScanMode = detailed ? "Detailed Scan" : "Smart Scan";
        await dbContext.SaveChangesAsync(cancellationToken);
        return segments;
    }

    private static IReadOnlyList<decimal> SelectSegmentStarts(decimal duration, decimal clipDuration, int count)
    {
        if (duration <= clipDuration || count <= 1)
        {
            return [0m];
        }

        var maxStart = Math.Max(0, duration - clipDuration);
        var raw = new SortedSet<decimal> { 0m, Math.Round(maxStart / 2, 3), Math.Round(maxStart, 3) };
        var step = maxStart / Math.Max(1, count - 1);
        for (var index = 0; index < count; index++)
        {
            raw.Add(Math.Round(Math.Clamp(index * step, 0, maxStart), 3));
        }

        var selected = new List<decimal>();
        foreach (var start in raw)
        {
            if (selected.Count == 0 || start - selected[^1] >= Math.Min(clipDuration, 1m))
            {
                selected.Add(start);
            }
            if (selected.Count == count)
            {
                break;
            }
        }

        return selected;
    }

    private async Task ProcessSegmentsAsync(
        AnalysisJob job,
        IReadOnlyList<AnalysisSegment> segments,
        string sourcePath,
        string workDirectory,
        CancellationToken cancellationToken)
    {
        await CheckForPauseOrCancellationAsync(job, "Before resolving provider mode", cancellationToken);
        var providerMode = await ResolveProviderModeAsync(job, cancellationToken);
        foreach (var segment in segments.Where(segment => segment.Status != AnalysisSegmentStatus.Completed))
        {
            await CheckForPauseOrCancellationAsync(job, $"Before analyzing part {segment.SegmentIndex} of {segments.Count}", cancellationToken);
            await ProcessSegmentAsync(job, segment, sourcePath, workDirectory, providerMode, cancellationToken);
        }
        var refreshed = await dbContext.AnalysisSegments
            .Where(segment => segment.AnalysisJobId == job.Id)
            .ToListAsync(cancellationToken);
        var completedCoverage = refreshed.Where(segment => segment.Status == AnalysisSegmentStatus.Completed).Sum(segment => segment.Duration);
        var requiredCoverage = refreshed.Sum(segment => segment.Duration) * _options.MinimumRequiredCoverageRatio;
        if (completedCoverage < requiredCoverage)
        {
            throw new ProcessingException("MINIMUM_COVERAGE_NOT_REACHED", "We could not analyze enough of this video. Please retry.");
        }
    }

    private async Task ProcessSegmentAsync(
        AnalysisJob job,
        AnalysisSegment segment,
        string sourcePath,
        string workDirectory,
        string providerMode,
        CancellationToken cancellationToken)
    {
        await CheckForPauseOrCancellationAsync(job, $"Before preparing part {segment.SegmentIndex} of {job.TotalSegments}", cancellationToken);
        segment.Status = AnalysisSegmentStatus.Preparing;
        segment.StartedAt ??= DateTimeOffset.UtcNow;
        segment.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var segmentDirectory = Path.Combine(workDirectory, "segments");
        Directory.CreateDirectory(segmentDirectory);
        var clipPath = Path.Combine(segmentDirectory, $"segment_{segment.SegmentIndex:000}.mp4");
        try
        {
            await CheckForPauseOrCancellationAsync(job, $"Before extracting part {segment.SegmentIndex} of {job.TotalSegments}", cancellationToken);
            await ExtractSegmentClipAsync(sourcePath, clipPath, segment.StartTime, segment.Duration, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, $"After extracting part {segment.SegmentIndex} of {job.TotalSegments}", cancellationToken);
            segment.LocalTemporaryPath = clipPath;
            segment.Status = AnalysisSegmentStatus.Analyzing;
            segment.Progress = 50;
            segment.LastActivityAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            await CheckForPauseOrCancellationAsync(job, $"Before provider request for part {segment.SegmentIndex} of {job.TotalSegments}", cancellationToken);
            segment.AttemptCount++;
            segment.LastActivityAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            var response = await aiInferenceClient.AnalyzeVideoAsync(
                new AiAnalyzeVideoRequest(job.VideoId, job.Id, job.Video.UserId, providerMode, clipPath, [], segment.SegmentIndex, segment.AttemptCount),
                cancellationToken);

            segment.AiScore = response.OverallAiScore;
            segment.Confidence = response.OverallConfidence;
            segment.ResultJson = JsonSerializer.Serialize(response, SerializerOptions);
            segment.ProviderRequestId = TryGetProviderRequestId(response.ExternalProviderResultJson);
            segment.Status = AnalysisSegmentStatus.Completed;
            segment.Progress = 100;
            segment.CompletedAt = DateTimeOffset.UtcNow;
            segment.LastActivityAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            await UpdateSegmentProgressAsync(job.Id, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, $"After provider response for part {segment.SegmentIndex} of {job.TotalSegments}", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await MarkSegmentCancelledAsync(segment, CancellationToken.None);
            throw new ProcessingException("CANCELLED", "Analysis was cancelled.");
        }
        catch (Exception exception) when (exception is AiServiceException or ProcessingException)
        {
            segment.Status = AnalysisSegmentStatus.Failed;
            segment.Progress = Math.Clamp(segment.Progress, 0, 99);
            segment.ErrorCode = exception is AiServiceException aiException ? aiException.ErrorCode : ((ProcessingException)exception).ErrorCode;
            segment.SafeErrorMessage = exception is AiServiceException ai ? ai.SafeMessage : ((ProcessingException)exception).SafeMessage;
            segment.FailedAt = DateTimeOffset.UtcNow;
            segment.LastActivityAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        finally
        {
            TryDeleteFile(clipPath);
        }
    }

    private async Task ExtractSegmentClipAsync(string sourcePath, string outputPath, decimal start, decimal duration, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("ffmpeg", _options.FfmpegPath, [
            "-y",
            "-ss", FormatSeconds(start),
            "-i", sourcePath,
            "-t", FormatSeconds(duration),
            "-map", "0:v:0",
            "-an",
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "23",
            outputPath
        ], cancellationToken);
        if (!result.Succeeded || !File.Exists(outputPath))
        {
            throw new ProcessingException("SEGMENT_EXTRACTION_FAILED", "We could not prepare part of this video for analysis.");
        }
    }

    private AiAnalyzeFramesResponse BuildAggregateResponse(AnalysisJob job, IReadOnlyList<AnalysisSegment> plannedSegments)
    {
        var completed = dbContext.AnalysisSegments
            .AsNoTracking()
            .Where(segment => segment.AnalysisJobId == job.Id && segment.Status == AnalysisSegmentStatus.Completed)
            .OrderBy(segment => segment.SegmentIndex)
            .ToList();
        if (completed.Count == 1 && !string.IsNullOrWhiteSpace(completed[0].ResultJson))
        {
            var direct = JsonSerializer.Deserialize<AiAnalyzeFramesResponse>(completed[0].ResultJson!, SerializerOptions);
            if (direct is not null)
            {
                return direct;
            }
        }

        var coverage = completed.Sum(segment => segment.Duration);
        var weightedScore = coverage <= 0 ? 0m : completed.Sum(segment => (segment.AiScore ?? 0m) * segment.Duration) / coverage;
        var weightedConfidence = coverage <= 0 ? 0m : completed.Sum(segment => (segment.Confidence ?? 0m) * segment.Duration) / coverage;
        var highRisk = completed.Any(segment => segment.AiScore >= _options.HighRiskOverrideScore);
        var finalScore = highRisk ? Math.Max(weightedScore, _options.HighRiskOverrideScore) : weightedScore;

        return new AiAnalyzeFramesResponse(
            job.VideoId,
            job.Id,
            "smart-scan-aggregate",
            "smart-scan-v1",
            "representative_video_segments",
            false,
            Math.Round(Math.Clamp(finalScore, 0m, 1m), 3),
            Math.Round(1m - Math.Clamp(finalScore, 0m, 1m), 3),
            Math.Round(Math.Clamp(weightedConfidence, 0m, 1m), 3),
            highRisk ? "Suspicious" : "Inconclusive",
            [],
            ["Smart Scan analyzes representative portions throughout the video; it does not claim every frame was analyzed."],
            highRisk ? ["One analyzed portion showed very high AI probability."] : [],
            StrongFrameEvidence: highRisk,
            MinimumRecommendedScore: highRisk ? _options.HighRiskOverrideScore : null,
            VideoComponentScore: Math.Round(weightedScore, 3),
            Provider: "SmartScan",
            ProviderMode: job.ScanMode ?? "Smart Scan",
            FinalDecisionSource: "SmartScan")
        {
            RawJson = JsonSerializer.Serialize(new
            {
                scan_mode = job.ScanMode ?? "Smart Scan",
                sampled = plannedSegments.Count > 1,
                analyzed_coverage_seconds = coverage,
                total_duration_seconds = job.TotalDurationSeconds,
                high_risk_override = highRisk,
                segments = completed.Select(segment => new
                {
                    segment.SegmentIndex,
                    segment.StartTime,
                    segment.EndTime,
                    segment.Duration,
                    segment.AiScore,
                    segment.Confidence
                })
            }, SerializerOptions)
        };
    }

    private async Task UpdateSegmentProgressAsync(long jobId, CancellationToken cancellationToken)
    {
        var job = await dbContext.AnalysisJobs.FirstAsync(existing => existing.Id == jobId, cancellationToken);
        var segments = await dbContext.AnalysisSegments.Where(segment => segment.AnalysisJobId == jobId).ToListAsync(cancellationToken);
        job.CompletedSegments = segments.Count(segment => segment.Status == AnalysisSegmentStatus.Completed);
        job.TotalSegments = segments.Count;
        job.AnalyzedCoverageSeconds = segments.Where(segment => segment.Status == AnalysisSegmentStatus.Completed).Sum(segment => segment.Duration);
        job.CurrentStep = $"Analyzing different parts - {job.CompletedSegments} of {job.TotalSegments}";
        job.Progress = Math.Clamp(25 + (int)Math.Round((job.CompletedSegments / (decimal)Math.Max(1, job.TotalSegments)) * 65m), 25, 90);
        job.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task CheckForPauseOrCancellationAsync(
        AnalysisJob job,
        string checkpoint,
        CancellationToken cancellationToken)
    {
        await dbContext.Entry(job).ReloadAsync(cancellationToken);
        job.LastCheckpoint = checkpoint;
        job.LastActivityAt = DateTimeOffset.UtcNow;

        if (job.CancelRequested || job.Status == JobStatus.CancelRequested)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            throw new ProcessingException("CANCELLED", "Analysis was cancelled.");
        }

        if (job.PauseRequested || job.Status == JobStatus.PauseRequested)
        {
            await MarkPausedAsync(job, checkpoint, cancellationToken);
            throw new AnalysisPausedException("Analysis paused.");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkPausedAsync(AnalysisJob job, string checkpoint, CancellationToken cancellationToken)
    {
        await dbContext.Entry(job).ReloadAsync(cancellationToken);
        if (job.CancelRequested || job.Status == JobStatus.CancelRequested)
        {
            throw new ProcessingException("CANCELLED", "Analysis was cancelled.");
        }

        var now = DateTimeOffset.UtcNow;
        job.Status = JobStatus.Paused;
        job.PauseRequested = false;
        job.PausedAt ??= now;
        job.PausedFromStage = checkpoint;
        job.LastCheckpoint = checkpoint;
        job.CurrentStep = BuildPausedCurrentStep(job, checkpoint);
        job.LastActivityAt = now;
        job.ResumeBackgroundJobId = null;
        if (job.Video is not null)
        {
            job.Video.Status = VideoStatus.Queued;
        }

        var activeSegments = await dbContext.AnalysisSegments
            .Where(segment => segment.AnalysisJobId == job.Id
                && (segment.Status == AnalysisSegmentStatus.Preparing || segment.Status == AnalysisSegmentStatus.Analyzing))
            .ToListAsync(cancellationToken);
        foreach (var segment in activeSegments)
        {
            segment.Status = AnalysisSegmentStatus.Pending;
            segment.Progress = Math.Clamp(segment.Progress, 0, 50);
            segment.LastActivityAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Analysis job {JobId} video {VideoId} reached safe pause checkpoint {Checkpoint}.",
            job.Id,
            job.VideoId,
            checkpoint);
        await jobLogService.LogAsync(job.Id, "Paused", "Information", "Analysis paused at a safe checkpoint.", new { checkpoint }, cancellationToken);
    }

    private async Task MarkCancelledAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        job.Status = JobStatus.Cancelled;
        job.PauseRequested = false;
        job.PauseRequestedAt = null;
        job.ResumeBackgroundJobId = null;
        job.CurrentStep = "Analysis cancelled";
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.LastActivityAt = DateTimeOffset.UtcNow;
        if (job.Video is not null)
        {
            job.Video.Status = VideoStatus.Cancelled;
        }

        var segments = await dbContext.AnalysisSegments
            .Where(segment => segment.AnalysisJobId == job.Id && segment.Status != AnalysisSegmentStatus.Completed)
            .ToListAsync(cancellationToken);
        foreach (var segment in segments)
        {
            segment.Status = AnalysisSegmentStatus.Cancelled;
            segment.LastActivityAt = DateTimeOffset.UtcNow;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await jobLogService.LogAsync(job.Id, "Cancelled", "Information", "Analysis was cancelled by the user.", null, cancellationToken);
    }

    private async Task MarkSegmentCancelledAsync(AnalysisSegment segment, CancellationToken cancellationToken)
    {
        segment.Status = AnalysisSegmentStatus.Cancelled;
        segment.LastActivityAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildPausedCurrentStep(AnalysisJob job, string checkpoint)
    {
        if (!string.IsNullOrWhiteSpace(job.ScanMode) && job.TotalSegments > 0)
        {
            return $"Paused during {job.ScanMode.ToLowerInvariant()}";
        }

        return $"Paused at {checkpoint.ToLowerInvariant()}";
    }

    private static void ValidateMetadata(ExtractedMetadataResult metadata)
    {
        if (metadata.DurationSeconds is null or <= 0 || string.IsNullOrWhiteSpace(metadata.Codec) || string.IsNullOrWhiteSpace(metadata.Resolution))
        {
            throw new ProcessingException("INVALID_VIDEO", "This file could not be processed as a valid video.");
        }
    }

    private static string? TryGetProviderRequestId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("provider_request_id", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatSeconds(decimal seconds)
    {
        return seconds.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private async Task RunInternalMatchingAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        if (!_internalMatchingOptions.Enabled)
        {
            return;
        }

        try
        {
            await CheckForPauseOrCancellationAsync(job, "Before internal frame matching", cancellationToken);
            await UpdateProgressAsync(job, 92, "Generating frame hashes", cancellationToken);
            await jobLogService.LogAsync(job.Id, "FrameHashGenerationStarted", "Information", "Frame hash generation started.", null, cancellationToken);
            var hashes = await frameHashService.GenerateHashesForVideoAsync(job.VideoId, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "After frame hash generation", cancellationToken);
            await jobLogService.LogAsync(job.Id, "FrameHashGenerationCompleted", "Information", "Frame hash generation completed.", new
            {
                hashCount = hashes.Count
            }, cancellationToken);

            await CheckForPauseOrCancellationAsync(job, "Before internal video matching", cancellationToken);
            await UpdateProgressAsync(job, 96, "Checking internal video matches", cancellationToken);
            await jobLogService.LogAsync(job.Id, "InternalMatchingStarted", "Information", "Internal video matching started.", null, cancellationToken);
            var matches = await internalVideoMatchingService.MatchVideoAsync(job.VideoId, cancellationToken);
            await CheckForPauseOrCancellationAsync(job, "After internal video matching", cancellationToken);
            await jobLogService.LogAsync(
                job.Id,
                matches.Count == 0 ? "InternalMatchingNoMatches" : "InternalMatchingCompleted",
                "Information",
                matches.Count == 0 ? "No internal video matches found." : "Internal video matching completed.",
                new { matchCount = matches.Count },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not AnalysisPausedException and not ProcessingException && !_internalMatchingOptions.FailJobOnMatchingError)
        {
            logger.LogWarning(exception, "Internal matching failed for analysis job {JobId}, but the job will continue.", job.Id);
            await jobLogService.LogAsync(
                job.Id,
                "InternalMatchingFailed",
                "Warning",
                "Internal matching could not be completed. Core AI analysis was completed.",
                new { errorCode = "INTERNAL_MATCHING_FAILED" },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not AnalysisPausedException and not ProcessingException)
        {
            logger.LogError(exception, "Internal matching failed for analysis job {JobId}.", job.Id);
            await MarkFailedAsync(
                job,
                "InternalMatchingFailed",
                "INTERNAL_MATCHING_FAILED",
                "Internal video matching failed.",
                cancellationToken);
            throw;
        }
    }

    private async Task<AiAnalyzeFramesResponse> AnalyzeAsync(
        AnalysisJob job,
        IReadOnlyList<VideoFrame> frames,
        string workDirectory,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        if (frames.Count == 0)
        {
            throw new AiServiceException("AI_ANALYSIS_FAILED", "AI analysis could not be completed because no frames were available.");
        }

        var selectedFrames = SelectFramesForAi(frames, Math.Max(1, _aiServiceOptions.MaxFramesPerRequest));
        var aiFrameDirectory = Path.Combine(workDirectory, "ai-frames");
        Directory.CreateDirectory(aiFrameDirectory);
        var requestFrames = new List<AiAnalyzeFrameItem>(selectedFrames.Count);

        foreach (var frame in selectedFrames)
        {
            var extension = Path.GetExtension(frame.FrameUrl);
            var destination = Path.Combine(aiFrameDirectory, $"frame_{frame.Id}{extension}");
            await objectStorageService.DownloadToAsync(frame.FrameUrl, destination, cancellationToken);
            var bytes = await File.ReadAllBytesAsync(destination, cancellationToken);
            requestFrames.Add(new AiAnalyzeFrameItem(
                frame.Id,
                frame.FrameUrl,
                frame.FrameIndex,
                frame.TimestampSeconds,
                Convert.ToBase64String(bytes)));
        }

        logger.LogInformation(
            "Sending {FrameCount} frames to AI service for video {VideoId} job {JobId}. Frame ids: {FrameIds}. Image payload present: {HasImages}.",
            requestFrames.Count,
            job.VideoId,
            job.Id,
            requestFrames.Take(5).Select(frame => frame.FrameId).ToArray(),
            requestFrames.Take(5).Select(frame => !string.IsNullOrWhiteSpace(frame.ImageBase64)).ToArray());

        var providerMode = await ResolveProviderModeAsync(job, cancellationToken);
        var useVideoEndpoint = !string.Equals(providerMode, "local", StringComparison.OrdinalIgnoreCase);
        logger.LogInformation(
            "Resolved AI provider mode {ProviderMode} for video {VideoId} job {JobId}; selected endpoint {Endpoint}.",
            providerMode,
            job.VideoId,
            job.Id,
            useVideoEndpoint ? _aiServiceOptions.AnalyzeVideoPath : _aiServiceOptions.AnalyzeFramesPath);
        if (useVideoEndpoint)
        {
            return await aiInferenceClient.AnalyzeVideoAsync(
                new AiAnalyzeVideoRequest(
                    job.VideoId,
                    job.Id,
                    job.Video.UserId,
                    providerMode,
                    sourcePath,
                    requestFrames),
                cancellationToken);
        }

        var request = new AiAnalyzeFramesRequest(job.VideoId, job.Id, requestFrames);
        var response = await aiInferenceClient.AnalyzeFramesAsync(request, cancellationToken);
        return response with { Provider = "Local", ProviderMode = "local", FinalDecisionSource = "Local" };
    }

    private async Task<string> ResolveProviderModeAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        var configuredMode = NormalizeProviderMode(_aiServiceOptions.ProviderMode);
        logger.LogInformation(
            "Resolving AI provider mode for job {JobId}: configured={ConfiguredMode}, bitmindEnabled={BitMindEnabled}, externalPolicy={ExternalPolicy}, localFallback={LocalFallback}.",
            job.Id,
            configuredMode,
            _aiServiceOptions.BitMindEnabled,
            _aiServiceOptions.ExternalProviderPolicy,
            _aiServiceOptions.LocalFallbackEnabled);
        if (configuredMode == "local"
            || !_aiServiceOptions.BitMindEnabled
            || string.Equals(_aiServiceOptions.ExternalProviderPolicy, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            return "local";
        }

        var now = DateTimeOffset.UtcNow;
        var usage = await GetOrCreateUsageAsync("BitMind", now.Year, now.Month, cancellationToken);
        if (usage.RequestCount >= usage.QuotaLimit)
        {
            await jobLogService.LogAsync(
                job.Id,
                "ExternalProviderSkipped",
                "Warning",
                "BitMind monthly quota reached. Local analysis will be used.",
                new { provider = "BitMind", usage.RequestCount, usage.QuotaLimit },
                cancellationToken);
            return "local";
        }

        return configuredMode;
    }

    private async Task<ApiUsageMonthly> GetOrCreateUsageAsync(string providerName, int year, int month, CancellationToken cancellationToken)
    {
        var usage = await dbContext.ApiUsageMonthly
            .FirstOrDefaultAsync(existing => existing.ProviderName == providerName && existing.Year == year && existing.Month == month, cancellationToken);
        if (usage is not null)
        {
            usage.QuotaLimit = _aiServiceOptions.BitMindMonthlyQuota;
            return usage;
        }

        usage = new ApiUsageMonthly
        {
            ProviderName = providerName,
            Year = year,
            Month = month,
            QuotaLimit = _aiServiceOptions.BitMindMonthlyQuota
        };
        dbContext.ApiUsageMonthly.Add(usage);
        await dbContext.SaveChangesAsync(cancellationToken);
        return usage;
    }

    private List<VideoFrame> SelectFramesForAi(IReadOnlyList<VideoFrame> frames, int maxFrames)
    {
        var ordered = frames
            .OrderBy(frame => frame.TimestampSeconds)
            .ThenBy(frame => frame.FrameIndex)
            .ToList();

        if (!string.Equals(_aiServiceOptions.FrameSamplingStrategy, "uniform", StringComparison.OrdinalIgnoreCase)
            || ordered.Count <= maxFrames)
        {
            return ordered.Take(maxFrames).ToList();
        }

        if (maxFrames == 1)
        {
            return [ordered[ordered.Count / 2]];
        }

        var selected = new List<VideoFrame>(maxFrames);
        var usedIndexes = new HashSet<int>();
        var step = (ordered.Count - 1) / (decimal)(maxFrames - 1);
        for (var index = 0; index < maxFrames; index++)
        {
            var sourceIndex = (int)Math.Round(index * step, MidpointRounding.AwayFromZero);
            sourceIndex = Math.Clamp(sourceIndex, 0, ordered.Count - 1);
            if (usedIndexes.Add(sourceIndex))
            {
                selected.Add(ordered[sourceIndex]);
            }
        }

        return selected;
    }

    private async Task<AiResult> SaveAiResultAsync(
        long videoId,
        AiAnalyzeFramesResponse aiResponse,
        FinalScoringResult scoringResult,
        CancellationToken cancellationToken)
    {
        var modelVersion = await GetOrCreateModelVersionAsync(aiResponse.ModelVersion, cancellationToken);
        var aiResult = new AiResult
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
        aiResult.Summary = BuildSummary(scoringResult.Summary, aiResponse, scoringResult.Warnings);
        aiResult.RawModelOutputJson = EnrichRawModelOutput(aiResponse, scoringResult);
        aiResult.Provider = aiResponse.Provider;
        aiResult.ProviderMode = aiResponse.ProviderMode;
        aiResult.FinalDecisionSource = aiResponse.FinalDecisionSource;
        aiResult.FallbackUsed = aiResponse.FallbackUsed;
        aiResult.FallbackReason = aiResponse.FallbackReason;
        aiResult.LocalResultJson = aiResponse.LocalResultJson;
        aiResult.HybridResultJson = aiResponse.BitMindResultJson is null
            ? null
            : JsonSerializer.Serialize(new { local = ParseJson(aiResponse.LocalResultJson), bitmind = ParseJson(aiResponse.BitMindResultJson) }, SerializerOptions);
        ApplyExternalProviderResult(aiResult, aiResponse.ExternalProviderResultJson);

        dbContext.AiResults.Add(aiResult);

        await dbContext.SaveChangesAsync(cancellationToken);
        await SaveProviderRequestAsync(videoId, aiResult, aiResponse, cancellationToken);
        return aiResult;
    }

    private static bool IsPureBitMindResult(AiAnalyzeFramesResponse aiResponse)
    {
        return string.Equals(aiResponse.ProviderMode, "bitmind", StringComparison.OrdinalIgnoreCase)
            && string.Equals(aiResponse.FinalDecisionSource, "BitMind", StringComparison.OrdinalIgnoreCase)
            && string.Equals(aiResponse.Provider, "BitMind", StringComparison.OrdinalIgnoreCase)
            && !aiResponse.FallbackUsed;
    }

    private static FinalScoringResult CreateBitMindScoringResult(AiAnalyzeFramesResponse aiResponse)
    {
        var score = Math.Clamp(aiResponse.OverallAiScore, 0m, 1m);
        var confidence = Math.Clamp(aiResponse.OverallConfidence, 0m, 1m);
        var label = NormalizeExternalLabel(aiResponse.LabelHint, score, confidence);
        return new FinalScoringResult(
            score,
            null,
            null,
            Math.Round(score, 3),
            Math.Round(confidence, 3),
            label,
            "BitMind external verification was used as the final decision source. Metadata is shown separately and was not used to overwrite the BitMind score.",
            []);
    }

    private static AnalysisLabel NormalizeExternalLabel(string? labelHint, decimal score, decimal confidence)
    {
        return labelHint?.Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant() switch
        {
            "likelyaigenerated" => AnalysisLabel.LikelyAiGenerated,
            "likelyreal" => AnalysisLabel.LikelyReal,
            "suspicious" => AnalysisLabel.Suspicious,
            "inconclusive" => AnalysisLabel.Inconclusive,
            _ when score >= 0.75m && confidence >= 0.60m => AnalysisLabel.LikelyAiGenerated,
            _ when score <= 0.25m && confidence >= 0.60m => AnalysisLabel.LikelyReal,
            _ when score >= 0.50m => AnalysisLabel.Suspicious,
            _ => AnalysisLabel.Inconclusive
        };
    }

    private async Task SaveProviderRequestAsync(
        long videoId,
        AiResult aiResult,
        AiAnalyzeFramesResponse aiResponse,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(aiResult.ExternalProviderName))
        {
            return;
        }

        var video = await dbContext.Videos.AsNoTracking().FirstAsync(existing => existing.Id == videoId, cancellationToken);
        var latestJobId = aiResponse.JobId;
        dbContext.AiProviderRequests.Add(new AiProviderRequest
        {
            VideoId = videoId,
            AnalysisJobId = latestJobId,
            AiResultId = aiResult.Id,
            UserId = video.UserId,
            ProviderName = aiResult.ExternalProviderName,
            ProviderMode = aiResult.ProviderMode,
            ProviderRequestId = aiResult.ExternalProviderResultId,
            ProviderJobId = aiResult.ExternalProviderJobId,
            Status = aiResult.ExternalProviderStatus ?? "Unknown",
            RequestStartedAt = aiResult.ExternalRequestedAt ?? aiResult.CreatedAt,
            RequestCompletedAt = aiResult.ExternalCompletedAt,
            DurationMs = aiResult.ExternalRequestedAt is not null && aiResult.ExternalCompletedAt is not null
                ? (long)(aiResult.ExternalCompletedAt.Value - aiResult.ExternalRequestedAt.Value).TotalMilliseconds
                : null,
            ErrorMessage = aiResult.ExternalErrorMessage,
            RawRequestMetadataJson = BuildProviderRequestMetadata(aiResponse),
            RawResponseJson = aiResult.ExternalRawResponseJson
        });

        var usage = await dbContext.ApiUsageMonthly
            .FirstOrDefaultAsync(existing => existing.ProviderName == aiResult.ExternalProviderName
                && existing.Year == aiResult.CreatedAt.Year
                && existing.Month == aiResult.CreatedAt.Month, cancellationToken);
        if (usage is not null)
        {
            if (string.Equals(aiResult.ExternalProviderStatus, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                usage.RequestCount++;
                usage.SuccessCount++;
            }
            else if (string.Equals(aiResult.ExternalProviderStatus, "Skipped", StringComparison.OrdinalIgnoreCase))
            {
                // Skipped external checks are not billable provider attempts.
            }
            else
            {
                usage.RequestCount++;
                usage.FailedCount++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildProviderRequestMetadata(AiAnalyzeFramesResponse aiResponse)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["provider_mode"] = aiResponse.ProviderMode,
            ["final_decision_source"] = aiResponse.FinalDecisionSource
        };

        if (!string.IsNullOrWhiteSpace(aiResponse.ExternalProviderResultJson))
        {
            try
            {
                using var document = JsonDocument.Parse(aiResponse.ExternalProviderResultJson);
                foreach (var propertyName in new[]
                {
                    "original_file_size_bytes",
                    "bitmind_file_size_bytes",
                    "compression_used",
                    "compression_attempts",
                    "compression_error",
                    "analysis_copy_path",
                    "provider_sent_file_name",
                    "provider_decision_band"
                })
                {
                    if (document.RootElement.TryGetProperty(propertyName, out var value))
                    {
                        metadata[propertyName] = value.ValueKind switch
                        {
                            JsonValueKind.String => value.GetString(),
                            JsonValueKind.Number when value.TryGetInt64(out var longValue) => longValue,
                            JsonValueKind.Number => value.GetDecimal(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null,
                            _ => value.GetRawText()
                        };
                    }
                }
            }
            catch
            {
                metadata["provider_metadata_parse_error"] = true;
            }
        }

        return JsonSerializer.Serialize(metadata, SerializerOptions);
    }

    private static void ApplyExternalProviderResult(AiResult aiResult, string? externalProviderResultJson)
    {
        if (string.IsNullOrWhiteSpace(externalProviderResultJson))
        {
            return;
        }

        using var document = JsonDocument.Parse(externalProviderResultJson);
        var root = document.RootElement;
        aiResult.ExternalProviderName = GetString(root, "provider_name");
        aiResult.ExternalProviderResultId = GetString(root, "provider_request_id");
        aiResult.ExternalProviderJobId = GetString(root, "provider_job_id");
        aiResult.ExternalProviderStatus = GetString(root, "provider_status");
        aiResult.ExternalLabel = GetString(root, "provider_label");
        aiResult.ExternalScore = GetDecimal(root, "provider_score");
        aiResult.ExternalConfidence = GetDecimal(root, "provider_confidence");
        aiResult.ExternalRawResponseJson = root.TryGetProperty("provider_raw_response", out var raw) ? raw.GetRawText() : externalProviderResultJson;
        aiResult.ExternalErrorMessage = GetString(root, "provider_error_message");
        aiResult.ExternalRequestedAt = GetDate(root, "provider_started_at");
        aiResult.ExternalCompletedAt = GetDate(root, "provider_completed_at");
    }

    private static string EnrichRawModelOutput(AiAnalyzeFramesResponse aiResponse, FinalScoringResult scoringResult)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(aiResponse.RawJson)
                ? JsonSerializer.Serialize(aiResponse, SerializerOptions)
                : aiResponse.RawJson);
            var root = document.RootElement.Clone();
            var payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(root.GetRawText(), SerializerOptions) ?? [];
            var debug = payload.TryGetValue("debug", out var existingDebug) && existingDebug is JsonElement debugElement && debugElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(debugElement.GetRawText(), SerializerOptions) ?? []
                : [];
            var scoring = debug.TryGetValue("scoring", out var existingScoring) && existingScoring is JsonElement scoringElement && scoringElement.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, object?>>(scoringElement.GetRawText(), SerializerOptions) ?? []
                : [];

            scoring["backend_final_score_after_weights"] = scoringResult.FinalScore;
            scoring["backend_confidence_after_adjustments"] = scoringResult.Confidence;
            scoring["backend_label"] = scoringResult.Label.ToString();
            scoring["backend_decision_warnings"] = scoringResult.Warnings;
            debug["scoring"] = scoring;
            if (scoringResult.Warnings.Count > 0)
            {
                var warnings = payload.TryGetValue("warnings", out var existingWarnings)
                    && existingWarnings is JsonElement warningsElement
                    && warningsElement.ValueKind == JsonValueKind.Array
                    ? warningsElement.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!)
                        .ToList()
                    : [];
                warnings.AddRange(scoringResult.Warnings);
                payload["warnings"] = warnings.Distinct().ToList();
            }
            payload["debug"] = debug;
            return JsonSerializer.Serialize(payload, SerializerOptions);
        }
        catch
        {
            return string.IsNullOrWhiteSpace(aiResponse.RawJson)
                ? JsonSerializer.Serialize(aiResponse, SerializerOptions)
                : aiResponse.RawJson;
        }
    }

    private static string BuildSummary(
        string baseSummary,
        AiAnalyzeFramesResponse aiResponse,
        IReadOnlyList<string> decisionWarnings)
    {
        var notes = new List<string> { baseSummary };
        if (aiResponse.IsMock)
        {
            notes.Add("Development mock model was used. This is not real AI detection.");
        }

        if (string.Equals(aiResponse.ModelCapability, "frame_image", StringComparison.OrdinalIgnoreCase))
        {
            notes.Add("Frame-level model used; temporal video consistency detection is limited.");
        }

        notes.AddRange(aiResponse.Warnings);
        notes.AddRange(decisionWarnings);
        return Truncate(string.Join(" ", notes.Distinct()), 1000) ?? baseSummary;
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
        var isResuming = job.Status == JobStatus.ResumeRequested || job.PausedAt is not null || job.Progress > 0;
        job.Status = JobStatus.Processing;
        job.Progress = isResuming ? Math.Max(job.Progress, 5) : 5;
        job.CurrentStep = isResuming ? "Resuming analysis" : "Preparing video for processing";
        job.StartedAt ??= DateTimeOffset.UtcNow;
        job.CompletedAt = null;
        job.FailedAt = null;
        job.FailedStage = null;
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.PauseRequested = false;
        job.PauseRequestedAt = null;
        job.ResumeBackgroundJobId = null;
        job.LastActivityAt = DateTimeOffset.UtcNow;
        job.Video.Status = VideoStatus.Processing;

        await dbContext.SaveChangesAsync(cancellationToken);
        await jobLogService.LogAsync(job.Id, isResuming ? "ProcessingResumed" : "PreparingVideo", "Information", isResuming ? "Analysis processing resumed." : "Analysis job started.", null, cancellationToken);
    }

    private async Task UpdateProgressAsync(
        AnalysisJob job,
        int progress,
        string currentStep,
        CancellationToken cancellationToken)
    {
        job.Progress = progress;
        job.CurrentStep = currentStep;
        job.LastActivityAt = DateTimeOffset.UtcNow;
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
        job.FailedStage = stepName;
        job.FailedAt = DateTimeOffset.UtcNow;
        job.LastActivityAt = DateTimeOffset.UtcNow;
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

    private static string NormalizeProviderMode(string? providerMode)
    {
        return providerMode?.Trim().ToLowerInvariant() switch
        {
            "bitmind" => "bitmind",
            "hybrid" => "hybrid",
            _ => "local"
        };
    }

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch
        {
            return json;
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static decimal? GetDecimal(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : null;
    }

    private static DateTimeOffset? GetDate(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), out var date)
            ? date
            : null;
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
