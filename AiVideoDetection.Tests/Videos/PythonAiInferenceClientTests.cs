using System.Net;
using System.Text;
using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Videos.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class PythonAiInferenceClientTests
{
    [Fact]
    public async Task SuccessfulAnalyzeResponseIsParsed()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
            {
              "video_id": 123,
              "job_id": 456,
              "model_version": "mock-video-ai-v1",
              "overall_ai_score": 0.62,
              "overall_confidence": 0.78,
              "label_hint": "Suspicious",
              "frames": [
                {
                  "frame_id": 1,
                  "frame_index": 1,
                  "timestamp_seconds": 2.0,
                  "ai_score": 0.64,
                  "confidence": 0.81,
                  "notes": ["mock"]
                }
              ],
              "notes": ["mock response"]
            }
            """)
        });

        var response = await client.AnalyzeFramesAsync(CreateRequest());

        Assert.Equal(123, response.VideoId);
        Assert.Equal("mock-video-ai-v1", response.ModelVersion);
        Assert.Equal(0.62m, response.OverallAiScore);
        Assert.Single(response.Frames);
    }

    [Fact]
    public async Task NonSuccessResponseThrowsCleanError()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.AnalyzeFramesAsync(CreateRequest()));

        Assert.Equal("AI_SERVICE_UNAVAILABLE", exception.ErrorCode);
    }

    [Fact]
    public async Task InvalidResponseThrowsCleanError()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("{ invalid json")
        });

        var exception = await Assert.ThrowsAsync<AiServiceException>(() => client.AnalyzeFramesAsync(CreateRequest()));

        Assert.Equal("AI_SERVICE_INVALID_RESPONSE", exception.ErrorCode);
    }

    private static PythonAiInferenceClient CreateClient(HttpResponseMessage response)
    {
        return new PythonAiInferenceClient(
            new FakeHttpClientFactory(new HttpClient(new StaticResponseHandler(response))),
            Options.Create(new AiServiceOptions { BaseUrl = "http://localhost:8000" }),
            NullLogger<PythonAiInferenceClient>.Instance);
    }

    private static AiAnalyzeFramesRequest CreateRequest()
    {
        return new AiAnalyzeFramesRequest(
            123,
            456,
            [new AiAnalyzeFrameItem(1, "frames/123/frame_000001.jpg", 1, 2)]);
    }

    private static StringContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return client;
        }
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
