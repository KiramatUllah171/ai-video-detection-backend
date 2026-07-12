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

            await UpdateProgressAsync(job, 78, "Preparing frames for AI analysis", cancellationToken);
            await jobLogService.LogAsync(job.Id, "AiAnalysisStarted", "Information", "AI frame analysis started.", null, cancellationToken);
            var savedFrames = await dbContext.VideoFrames
                .Where(frame => frame.VideoId == job.VideoId)
                .OrderBy(frame => frame.FrameIndex)
                .ToListAsync(cancellationToken);
            var aiResponse = await AnalyzeAsync(job, savedFrames, workDirectory, sourcePath, cancellationToken);
            await UpdateProgressAsync(job, 82, "Running AI detection model", cancellationToken);
            await jobLogService.LogAsync(job.Id, "AiAnalysisCompleted", "Information", "AI frame analysis completed.", new
            {
                aiResponse.ModelId,
                aiResponse.ModelVersion,
                aiResponse.ModelCapability,
                aiResponse.IsMock,
                aiResponse.OverallAiScore,
                aiResponse.OverallConfidence
            }, cancellationToken);

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
                    savedFrames.Count,
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
            await UpdateProgressAsync(job, 88, "Calculating authenticity score", cancellationToken);
            await jobLogService.LogAsync(job.Id, "ScoringCompleted", "Information", "Final scoring completed.", new
            {
                scoringResult.FinalScore,
                scoringResult.Confidence,
                label = scoringResult.Label.ToString()
            }, cancellationToken);

            var aiResult = await SaveAiResultAsync(job.VideoId, aiResponse, scoringResult, cancellationToken);
            await UpdateProgressAsync(job, 92, "Generating evidence", cancellationToken);
            var evidenceItems = evidenceGenerationService.GenerateEvidence(new EvidenceGenerationInput(
                aiResponse.ModelVersion,
                aiResponse.Frames,
                savedFrames.ToDictionary(frame => frame.Id, frame => frame.Id),
                metadataWarnings,
                scoringResult,
                aiResponse.ModelDisagreement,
                aiResponse.StrongFrameEvidence));
            await ReplaceEvidenceItemsAsync(aiResult, evidenceItems, cancellationToken);
            await jobLogService.LogAsync(job.Id, "EvidenceGenerated", "Information", "Evidence items generated.", new
            {
                evidenceCount = evidenceItems.Count
            }, cancellationToken);

            await RunInternalMatchingAsync(job, cancellationToken);

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

    private async Task RunInternalMatchingAsync(AnalysisJob job, CancellationToken cancellationToken)
    {
        if (!_internalMatchingOptions.Enabled)
        {
            return;
        }

        try
        {
            await UpdateProgressAsync(job, 92, "Generating frame hashes", cancellationToken);
            await jobLogService.LogAsync(job.Id, "FrameHashGenerationStarted", "Information", "Frame hash generation started.", null, cancellationToken);
            var hashes = await frameHashService.GenerateHashesForVideoAsync(job.VideoId, cancellationToken);
            await jobLogService.LogAsync(job.Id, "FrameHashGenerationCompleted", "Information", "Frame hash generation completed.", new
            {
                hashCount = hashes.Count
            }, cancellationToken);

            await UpdateProgressAsync(job, 96, "Checking internal video matches", cancellationToken);
            await jobLogService.LogAsync(job.Id, "InternalMatchingStarted", "Information", "Internal video matching started.", null, cancellationToken);
            var matches = await internalVideoMatchingService.MatchVideoAsync(job.VideoId, cancellationToken);
            await jobLogService.LogAsync(
                job.Id,
                matches.Count == 0 ? "InternalMatchingNoMatches" : "InternalMatchingCompleted",
                "Information",
                matches.Count == 0 ? "No internal video matches found." : "Internal video matching completed.",
                new { matchCount = matches.Count },
                cancellationToken);
        }
        catch (Exception exception) when (!_internalMatchingOptions.FailJobOnMatchingError)
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
        catch (Exception exception)
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

        usage.RequestCount++;
        await dbContext.SaveChangesAsync(cancellationToken);
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
                usage.SuccessCount++;
            }
            else if (string.Equals(aiResult.ExternalProviderStatus, "Skipped", StringComparison.OrdinalIgnoreCase))
            {
                usage.RequestCount = Math.Max(0, usage.RequestCount - 1);
            }
            else
            {
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
