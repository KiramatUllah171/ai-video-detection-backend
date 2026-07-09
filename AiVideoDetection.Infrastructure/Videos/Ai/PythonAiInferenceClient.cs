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
                    frame.TimestampSeconds))
                .ToList());

        try
        {
            using var response = await client.PostAsJsonAsync(_options.AnalyzeFramesPath, payload, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "AI service returned non-success status {StatusCode} for video {VideoId} job {JobId}.",
                    response.StatusCode,
                    request.VideoId,
                    request.JobId);
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
        [property: JsonPropertyName("timestamp_seconds")] decimal? TimestampSeconds);

    private sealed record AnalyzeFramesHttpResponse(
        [property: JsonPropertyName("video_id")] long VideoId,
        [property: JsonPropertyName("job_id")] long JobId,
        [property: JsonPropertyName("model_version")] string ModelVersion,
        [property: JsonPropertyName("overall_ai_score")] decimal OverallAiScore,
        [property: JsonPropertyName("overall_confidence")] decimal OverallConfidence,
        [property: JsonPropertyName("label_hint")] string LabelHint,
        [property: JsonPropertyName("frames")] IReadOnlyList<FrameAnalysisHttpResponse> Frames,
        [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes)
    {
        public AiAnalyzeFramesResponse ToApplicationResponse(string rawJson)
        {
            return new AiAnalyzeFramesResponse(
                VideoId,
                JobId,
                ModelVersion,
                OverallAiScore,
                OverallConfidence,
                LabelHint,
                Frames.Select(frame => new AiFrameAnalysisResult(
                    frame.FrameId,
                    frame.FrameIndex,
                    frame.TimestampSeconds,
                    frame.AiScore,
                    frame.Confidence,
                    frame.Notes)).ToList(),
                Notes)
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
        [property: JsonPropertyName("confidence")] decimal Confidence,
        [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes);
}
