using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Ai;

public class PythonAiInferenceClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AiServiceOptions> options,
    IProviderRequestGate providerRequestGate,
    IProviderCircuitBreaker providerCircuitBreaker,
    ILogger<PythonAiInferenceClient> logger) : IAiInferenceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex SensitiveFieldRegex = new(
        "(\"(?:api[_-]?key|access[_-]?token|token|authorization|secret)\"\\s*:\\s*\")[^\"]+\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private readonly AiServiceOptions _options = options.Value;

    public async Task<AiAnalyzeFramesResponse> AnalyzeFramesAsync(
        AiAnalyzeFramesRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var payload = new AnalyzeFramesHttpRequest(
            request.VideoId,
            request.JobId,
            request.Frames
                .Take(Math.Max(_options.MaxFramesPerRequest, 1))
                .Select(frame => new AnalyzeFrameHttpItem(
                    frame.FrameId,
                    frame.FrameUrl,
                    frame.FrameIndex,
                    frame.TimestampSeconds,
                    frame.ImageBase64))
                .ToList());

        try
        {
            logger.LogInformation(
                "Calling AI service endpoint {Endpoint} using local frame mode for video {VideoId} job {JobId}.",
                _options.AnalyzeFramesPath,
                request.VideoId,
                request.JobId);
            using var response = await client.PostAsJsonAsync(_options.AnalyzeFramesPath, payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var errorResponse = TryDeserializeError(errorJson);
                logger.LogWarning(
                    "AI service returned non-success status {StatusCode} with error {ErrorCode} for video {VideoId} job {JobId}.",
                    response.StatusCode,
                    errorResponse?.ErrorCode,
                    request.VideoId,
                    request.JobId);
                if (errorResponse is not null)
                {
                    throw new AiServiceException(errorResponse.ErrorCode, SafeMessage(errorResponse.ErrorCode, errorResponse.Message));
                }

                throw new AiServiceException(
                    response.StatusCode switch
                    {
                        HttpStatusCode.Unauthorized => "AI_SERVICE_UNAUTHORIZED",
                        HttpStatusCode.RequestTimeout => "AI_SERVICE_TIMEOUT",
                        _ => "AI_SERVICE_UNAVAILABLE"
                    },
                    response.StatusCode == HttpStatusCode.Unauthorized
                        ? "AI analysis service is not accepting backend requests."
                        : "AI analysis service is currently unavailable. Please try again later.");
            }

            var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var serviceResponse = JsonSerializer.Deserialize<AnalyzeFramesHttpResponse>(rawJson, JsonOptions);
            if (serviceResponse is null)
            {
                throw new AiServiceException("AI_SERVICE_INVALID_RESPONSE", "AI analysis service returned an invalid response.");
            }

            return serviceResponse.ToApplicationResponse(rawJson);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiServiceException("AI_SERVICE_TIMEOUT", "AI analysis service timed out. Please try again later.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new AiServiceException("AI_SERVICE_UNAVAILABLE", "AI analysis service is currently unavailable. Please try again later.", exception);
        }
        catch (JsonException exception)
        {
            throw new AiServiceException("AI_SERVICE_INVALID_RESPONSE", "AI analysis service returned an invalid response.", exception);
        }
    }

    public async Task<AiAnalyzeFramesResponse> AnalyzeVideoAsync(
        AiAnalyzeVideoRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient();
        var payload = new AnalyzeVideoHttpRequest(
            request.VideoId,
            request.JobId,
            request.ProviderMode,
            request.UserId,
            request.OriginalVideoPath,
            request.Frames
                .Take(Math.Max(_options.MaxFramesPerRequest, 1))
                .Select(frame => new AnalyzeFrameHttpItem(
                    frame.FrameId,
                    frame.FrameUrl,
                    frame.FrameIndex,
                    frame.TimestampSeconds,
                    frame.ImageBase64))
                .ToList());

        var circuitKey = ResolveCircuitKey(request.ProviderMode);
        if (!providerCircuitBreaker.CanExecute(circuitKey, DateTimeOffset.UtcNow))
        {
            throw new AiServiceException(
                "AI_SERVICE_CIRCUIT_OPEN",
                "The external analysis service is temporarily paused after repeated failures. Please try again shortly.");
        }

        var maxAttempts = Math.Max(1, _options.TransientRetryCount + 1);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await using var lease = await providerRequestGate.EnterAsync(
                request.VideoId,
                request.JobId,
                request.SegmentIndex,
                request.SegmentAttempt,
                request.ProviderMode,
                cancellationToken);
            var started = Stopwatch.GetTimestamp();
            try
            {
                logger.LogInformation(
                    "Calling AI service endpoint {Endpoint} using provider mode {ProviderMode} for video {VideoId} job {JobId} segment {SegmentIndex} provider attempt {ProviderAttempt}/{MaxProviderAttempts}.",
                    _options.AnalyzeVideoPath,
                    request.ProviderMode,
                    request.VideoId,
                    request.JobId,
                    request.SegmentIndex,
                    attempt,
                    maxAttempts);
                using var response = await client.PostAsJsonAsync(_options.AnalyzeVideoPath, payload, JsonOptions, cancellationToken);
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                if (!response.IsSuccessStatusCode)
                {
                    var errorJson = await response.Content.ReadAsStringAsync(cancellationToken);
                    var errorResponse = TryDeserializeError(errorJson);
                    var errorCode = MapVideoServiceErrorCode(response.StatusCode, errorResponse?.ErrorCode);
                    logger.LogWarning(
                        "AI video service returned non-success status {StatusCode} with error {ErrorCode} for video {VideoId} job {JobId} segment {SegmentIndex} segment attempt {SegmentAttempt} provider attempt {ProviderAttempt}/{MaxProviderAttempts} after {ElapsedMilliseconds} ms. Response summary: {ResponseSummary}",
                        (int)response.StatusCode,
                        errorCode,
                        request.VideoId,
                        request.JobId,
                        request.SegmentIndex,
                        request.SegmentAttempt,
                        attempt,
                        maxAttempts,
                        Math.Round(elapsedMs),
                        SummarizeProviderError(errorJson));

                    if (IsTransientStatusCode(response.StatusCode) && attempt < maxAttempts)
                    {
                        await DelayBeforeRetryAsync(response, attempt, cancellationToken);
                        continue;
                    }

                    if (IsCircuitBreakerFailure(response.StatusCode))
                    {
                        RecordProviderFailure(circuitKey);
                    }

                    throw new AiServiceException(
                        errorCode,
                        errorResponse is null
                            ? SafeMessage(errorCode, string.Empty)
                            : SafeMessage(errorResponse.ErrorCode, errorResponse.Message));
                }

                var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
                var serviceResponse = JsonSerializer.Deserialize<AnalyzeFramesHttpResponse>(rawJson, JsonOptions);
                if (serviceResponse is null)
                {
                    RecordProviderFailure(circuitKey);
                    throw new AiServiceException("AI_SERVICE_INVALID_RESPONSE", "AI analysis service returned an invalid response.");
                }

                logger.LogInformation(
                    "AI video service completed for video {VideoId} job {JobId} segment {SegmentIndex} segment attempt {SegmentAttempt} provider attempt {ProviderAttempt}/{MaxProviderAttempts} in {ElapsedMilliseconds} ms.",
                    request.VideoId,
                    request.JobId,
                    request.SegmentIndex,
                    request.SegmentAttempt,
                    attempt,
                    maxAttempts,
                    Math.Round(elapsedMs));
                providerCircuitBreaker.RecordSuccess(circuitKey);
                return serviceResponse.ToApplicationResponse(rawJson);
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                logger.LogWarning(
                    exception,
                    "AI video service timed out for video {VideoId} job {JobId} segment {SegmentIndex} segment attempt {SegmentAttempt} provider attempt {ProviderAttempt}/{MaxProviderAttempts} after {ElapsedMilliseconds} ms.",
                    request.VideoId,
                    request.JobId,
                    request.SegmentIndex,
                    request.SegmentAttempt,
                    attempt,
                    maxAttempts,
                    Math.Round(elapsedMs));
                if (attempt < maxAttempts)
                {
                    await DelayBeforeRetryAsync(null, attempt, cancellationToken);
                    continue;
                }

                RecordProviderFailure(circuitKey);
                throw new AiServiceException("AI_SERVICE_TIMEOUT", "AI analysis service timed out. Please try again later.", exception);
            }
            catch (HttpRequestException exception)
            {
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                logger.LogWarning(
                    exception,
                    "AI video service request failed for video {VideoId} job {JobId} segment {SegmentIndex} segment attempt {SegmentAttempt} provider attempt {ProviderAttempt}/{MaxProviderAttempts} after {ElapsedMilliseconds} ms.",
                    request.VideoId,
                    request.JobId,
                    request.SegmentIndex,
                    request.SegmentAttempt,
                    attempt,
                    maxAttempts,
                    Math.Round(elapsedMs));
                if (attempt < maxAttempts)
                {
                    await DelayBeforeRetryAsync(null, attempt, cancellationToken);
                    continue;
                }

                RecordProviderFailure(circuitKey);
                throw new AiServiceException("AI_SERVICE_UNAVAILABLE", "AI analysis service is currently unavailable. Please try again later.", exception);
            }
            catch (JsonException exception)
            {
                RecordProviderFailure(circuitKey);
                throw new AiServiceException("AI_SERVICE_INVALID_RESPONSE", "AI analysis service returned an invalid response.", exception);
            }
        }

        RecordProviderFailure(circuitKey);
        throw new AiServiceException("AI_VIDEO_SERVICE_UNAVAILABLE", "External video analysis is temporarily unavailable.");
    }

    public async Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await CreateClient().GetAsync(_options.HealthPath, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "AI service health check failed.");
            return false;
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("AiService");
        client.BaseAddress ??= new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 600));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-AI-Service-Key");
            client.DefaultRequestHeaders.Add("X-AI-Service-Key", _options.ApiKey);
        }

        return client;
    }

    private async Task DelayBeforeRetryAsync(HttpResponseMessage? response, int attempt, CancellationToken cancellationToken)
    {
        var retryAfter = response?.Headers.RetryAfter?.Delta;
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 6);
        var baseDelaySeconds = Math.Max(1, _options.TransientRetryBackoffSeconds);
        var exponentialDelay = TimeSpan.FromSeconds(Math.Min(120, baseDelaySeconds * Math.Pow(2, exponent)));
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(100, 500));
        var delay = retryAfter is { TotalSeconds: > 0 }
            ? retryAfter.Value
            : exponentialDelay.Add(jitter);
        await Task.Delay(delay, cancellationToken);
    }

    private static bool IsTransientStatusCode(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;
    }

    private static bool IsCircuitBreakerFailure(HttpStatusCode statusCode)
    {
        return IsTransientStatusCode(statusCode);
    }

    private void RecordProviderFailure(string circuitKey)
    {
        providerCircuitBreaker.RecordFailure(
            circuitKey,
            DateTimeOffset.UtcNow,
            _options.CircuitBreakerFailureThreshold,
            TimeSpan.FromSeconds(Math.Clamp(_options.CircuitBreakerBreakSeconds, 10, 3600)));
    }

    private string ResolveCircuitKey(string providerMode)
    {
        var normalized = providerMode.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        return normalized.Equals("bitmind", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("external", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("hybrid", StringComparison.OrdinalIgnoreCase)
            ? "BitMind"
            : "Internal";
    }

    private static string MapVideoServiceErrorCode(HttpStatusCode statusCode, string? providerErrorCode)
    {
        if (!string.IsNullOrWhiteSpace(providerErrorCode))
        {
            return providerErrorCode;
        }

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "AI_SERVICE_UNAUTHORIZED",
            HttpStatusCode.Forbidden => "AI_SERVICE_UNAUTHORIZED",
            HttpStatusCode.RequestTimeout => "AI_SERVICE_TIMEOUT",
            HttpStatusCode.TooManyRequests => "BITMIND_RATE_LIMITED",
            HttpStatusCode.BadRequest => "AI_SERVICE_INVALID_REQUEST",
            HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => "AI_VIDEO_SERVICE_UNAVAILABLE",
            _ => "AI_VIDEO_SERVICE_UNAVAILABLE"
        };
    }

    private static string SummarizeProviderError(string? errorJson)
    {
        if (string.IsNullOrWhiteSpace(errorJson))
        {
            return "empty";
        }

        var sanitized = errorJson
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        sanitized = SensitiveFieldRegex.Replace(sanitized, "$1[redacted]\"");
        return sanitized.Length <= 300 ? sanitized : sanitized[..300];
    }

    private sealed record AnalyzeFramesHttpRequest(
        [property: JsonPropertyName("video_id")] long VideoId,
        [property: JsonPropertyName("job_id")] long JobId,
        [property: JsonPropertyName("frames")] IReadOnlyList<AnalyzeFrameHttpItem> Frames);

    private sealed record AnalyzeVideoHttpRequest(
        [property: JsonPropertyName("video_id")] long VideoId,
        [property: JsonPropertyName("job_id")] long JobId,
        [property: JsonPropertyName("provider_mode")] string ProviderMode,
        [property: JsonPropertyName("user_id")] long UserId,
        [property: JsonPropertyName("original_video_path")] string OriginalVideoPath,
        [property: JsonPropertyName("frames")] IReadOnlyList<AnalyzeFrameHttpItem> Frames);

    private sealed record AnalyzeFrameHttpItem(
        [property: JsonPropertyName("frame_id")] long FrameId,
        [property: JsonPropertyName("frame_url")] string FrameUrl,
        [property: JsonPropertyName("frame_index")] int FrameIndex,
        [property: JsonPropertyName("timestamp_seconds")] decimal? TimestampSeconds,
        [property: JsonPropertyName("image_base64")] string? ImageBase64);

    private sealed record AnalyzeFramesHttpResponse(
        [property: JsonPropertyName("video_id")] long VideoId,
        [property: JsonPropertyName("job_id")] long JobId,
        [property: JsonPropertyName("model_id")] string? ModelId,
        [property: JsonPropertyName("model_version")] string ModelVersion,
        [property: JsonPropertyName("model_capability")] string? ModelCapability,
        [property: JsonPropertyName("is_mock")] bool IsMock,
        [property: JsonPropertyName("overall_ai_score")] decimal OverallAiScore,
        [property: JsonPropertyName("real_probability")] decimal RealProbability,
        [property: JsonPropertyName("overall_confidence")] decimal OverallConfidence,
        [property: JsonPropertyName("label_hint")] string LabelHint,
        [property: JsonPropertyName("frames")] IReadOnlyList<FrameAnalysisHttpResponse> Frames,
        [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes,
        [property: JsonPropertyName("warnings")] IReadOnlyList<string>? Warnings,
        [property: JsonPropertyName("model_disagreement")] bool ModelDisagreement,
        [property: JsonPropertyName("strong_frame_evidence")] bool StrongFrameEvidence,
        [property: JsonPropertyName("minimum_recommended_score")] decimal? MinimumRecommendedScore,
        [property: JsonPropertyName("ensemble_strategy")] string? EnsembleStrategy,
        [property: JsonPropertyName("component_scores")] JsonElement? ComponentScores,
        [property: JsonPropertyName("provider")] string? Provider,
        [property: JsonPropertyName("provider_mode")] string? ProviderMode,
        [property: JsonPropertyName("external_provider_result")] JsonElement? ExternalProviderResult,
        [property: JsonPropertyName("fallback_used")] bool FallbackUsed,
        [property: JsonPropertyName("fallback_reason")] string? FallbackReason,
        [property: JsonPropertyName("local_result")] JsonElement? LocalResult,
        [property: JsonPropertyName("bitmind_result")] JsonElement? BitMindResult,
        [property: JsonPropertyName("final_decision_source")] string? FinalDecisionSource)
    {
        public AiAnalyzeFramesResponse ToApplicationResponse(string rawJson)
        {
            var componentScoresJson = ComponentScores?.GetRawText();
            return new AiAnalyzeFramesResponse(
                VideoId,
                JobId,
                ModelId ?? ModelVersion,
                ModelVersion,
                ModelCapability ?? "unknown",
                IsMock,
                OverallAiScore,
                RealProbability,
                OverallConfidence,
                LabelHint,
                Frames.Select(frame => new AiFrameAnalysisResult(
                    frame.FrameId,
                    frame.FrameIndex,
                    frame.TimestampSeconds,
                    frame.AiScore,
                    frame.RealProbability,
                    frame.Confidence,
                    frame.Notes)).ToList(),
                Notes,
                Warnings ?? [],
                ModelDisagreement,
                StrongFrameEvidence,
                MinimumRecommendedScore,
                EnsembleStrategy,
                componentScoresJson,
                ExtractComponentScore(componentScoresJson, "video"),
                ExtractComponentScore(componentScoresJson, "frame"),
                ExtractNestedComponentScore(componentScoresJson, "frame", "raw_frame_ai_score"),
                ExtractNestedComponentScore(componentScoresJson, "frame", "calibrated_frame_ai_score"),
                ExtractNestedComponentScore(componentScoresJson, "frame", "reliability", "accuracy"),
                Provider ?? "Local",
                ProviderMode ?? "local",
                ExternalProviderResult?.GetRawText(),
                FallbackUsed,
                FallbackReason,
                LocalResult?.GetRawText(),
                BitMindResult?.GetRawText(),
                FinalDecisionSource ?? "Local")
            {
                RawJson = rawJson
            };
        }
    }

    private sealed record FrameAnalysisHttpResponse(
        [property: JsonPropertyName("frame_id")] long FrameId,
        [property: JsonPropertyName("frame_index")] int FrameIndex,
        [property: JsonPropertyName("timestamp_seconds")] decimal? TimestampSeconds,
        [property: JsonPropertyName("ai_score")] decimal AiScore,
        [property: JsonPropertyName("real_probability")] decimal RealProbability,
        [property: JsonPropertyName("confidence")] decimal Confidence,
        [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes);

    private sealed record AiErrorHttpResponse(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("error_code")] string ErrorCode,
        [property: JsonPropertyName("message")] string Message);

    private static AiErrorHttpResponse? TryDeserializeError(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AiErrorHttpResponse>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeMessage(string errorCode, string fallback)
    {
        return errorCode switch
        {
            "REAL_MODEL_NOT_CONFIGURED" => "AI detection model is not configured. Configure a model or enable mock mode for development.",
            "FRAME_IMAGE_REQUIRED" => "AI analysis could not be completed because frame images were not available.",
            "MODEL_LOAD_FAILED" => "AI detection model could not be loaded.",
            "AI_SERVICE_INVALID_RESPONSE" => "AI analysis service returned an invalid response.",
            "BITMIND_AUTH_FAILED" => "External video analysis is temporarily unavailable.",
            "BITMIND_FORBIDDEN" => "External video analysis is temporarily unavailable.",
            "BITMIND_RATE_LIMITED" => "The analysis service is currently busy. Please wait a moment and retry.",
            "BITMIND_UNAVAILABLE" => "External video analysis is temporarily unavailable.",
            "AI_SERVICE_UNAUTHORIZED" => "AI analysis service is not accepting backend requests.",
            "AI_SERVICE_TIMEOUT" => "The external analysis service took too long to respond. Please retry.",
            "AI_SERVICE_INVALID_REQUEST" => "External video analysis could not accept the prepared video segment.",
            "AI_VIDEO_SERVICE_UNAVAILABLE" => "External video analysis is temporarily unavailable.",
            "AI_SERVICE_CIRCUIT_OPEN" => "The external analysis service is temporarily paused after repeated failures. Please try again shortly.",
            _ => string.IsNullOrWhiteSpace(fallback) ? "AI analysis could not be completed for this video." : fallback
        };
    }

    private static decimal? ExtractComponentScore(string? componentScoresJson, string componentName)
    {
        if (string.IsNullOrWhiteSpace(componentScoresJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(componentScoresJson);
            return document.RootElement.TryGetProperty(componentName, out var component)
                && component.ValueKind == JsonValueKind.Object
                && component.TryGetProperty("ai_score", out var score)
                && score.ValueKind == JsonValueKind.Number
                ? score.GetDecimal()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static decimal? ExtractNestedComponentScore(
        string? componentScoresJson,
        string componentName,
        string propertyName,
        string? nestedPropertyName = null)
    {
        if (string.IsNullOrWhiteSpace(componentScoresJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(componentScoresJson);
            if (!document.RootElement.TryGetProperty(componentName, out var component)
                || component.ValueKind != JsonValueKind.Object
                || !component.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (nestedPropertyName is not null)
            {
                if (value.ValueKind != JsonValueKind.Object
                    || !value.TryGetProperty(nestedPropertyName, out value))
                {
                    return null;
                }
            }

            return value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;
        }
        catch
        {
            return null;
        }
    }
}
