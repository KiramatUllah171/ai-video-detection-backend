using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Ai;

public class PythonAiInferenceClient(
    IHttpClientFactory httpClientFactory,
    IOptions<AiServiceOptions> options,
    ILogger<PythonAiInferenceClient> logger) : IAiInferenceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
                    response.StatusCode == HttpStatusCode.RequestTimeout ? "AI_SERVICE_TIMEOUT" : "AI_SERVICE_UNAVAILABLE",
                    "AI analysis service is currently unavailable. Please try again later.");
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
        client.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");
        client.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 1, 600));
        return client;
    }

    private sealed record AnalyzeFramesHttpRequest(
        [property: JsonPropertyName("video_id")] long VideoId,
        [property: JsonPropertyName("job_id")] long JobId,
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
        [property: JsonPropertyName("component_scores")] JsonElement? ComponentScores)
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
                ExtractNestedComponentScore(componentScoresJson, "frame", "reliability", "accuracy"))
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
