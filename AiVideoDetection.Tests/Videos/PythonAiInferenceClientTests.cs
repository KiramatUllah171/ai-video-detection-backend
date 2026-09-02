using System.Net;
using System.Text;
using AiVideoDetection.Application.Videos.Ai;
using AiVideoDetection.Application.Videos.Interfaces;
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

    [Fact]
    public async Task AnalyzeVideoParsesProviderFields()
    {
        var client = CreateClient(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
            {
              "video_id": 123,
              "job_id": 456,
              "model_id": "bitmind-subnet-34",
              "model_version": "bitmind-oracle-v1-sn34",
              "model_capability": "external_video",
              "is_mock": false,
              "overall_ai_score": 0.88,
              "real_probability": 0.12,
              "overall_confidence": 0.91,
              "label_hint": "LikelyAiGenerated",
              "frames": [],
              "notes": ["external"],
              "warnings": ["privacy"],
              "provider": "BitMind",
              "provider_mode": "bitmind",
              "final_decision_source": "BitMind",
              "external_provider_result": {
                "provider_name": "BitMind",
                "provider_status": "Completed",
                "provider_score": 0.88,
                "provider_confidence": 0.91
              }
            }
            """)
        });

        var response = await client.AnalyzeVideoAsync(new AiAnalyzeVideoRequest(123, 456, 7, "bitmind", "source.mp4", []));

        Assert.Equal("BitMind", response.Provider);
        Assert.Equal("bitmind", response.ProviderMode);
        Assert.Equal("BitMind", response.FinalDecisionSource);
        Assert.Contains("provider_status", response.ExternalProviderResultJson);
        Assert.Contains("Completed", response.ExternalProviderResultJson);
    }

    [Fact]
    public async Task AnalyzeVideoRetriesTransientProviderFailures()
    {
        var handler = new SequenceResponseHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = JsonContent("""{"success":false,"error_code":"BITMIND_RATE_LIMITED","message":"busy"}""")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""
                {
                  "video_id": 123,
                  "job_id": 456,
                  "model_version": "bitmind-oracle-v1-sn34",
                  "overall_ai_score": 0.60,
                  "real_probability": 0.40,
                  "overall_confidence": 0.70,
                  "label_hint": "Suspicious",
                  "frames": [],
                  "notes": [],
                  "provider": "BitMind",
                  "provider_mode": "bitmind"
                }
                """)
            });
        var client = CreateClient(handler, new AiServiceOptions
        {
            BaseUrl = "http://localhost:8000",
            TransientRetryCount = 1,
            TransientRetryBackoffSeconds = 1
        });

        var response = await client.AnalyzeVideoAsync(new AiAnalyzeVideoRequest(123, 456, 7, "bitmind", "source.mp4", [], 2, 1));

        Assert.Equal("BitMind", response.Provider);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task SendsConfiguredAiServiceApiKeyHeader()
    {
        var handler = new StaticResponseHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent("""
            {
              "video_id": 123,
              "job_id": 456,
              "model_version": "mock-video-ai-v1",
              "is_mock": true,
              "overall_ai_score": 0.20,
              "real_probability": 0.80,
              "overall_confidence": 0.70,
              "label_hint": "LikelyReal",
              "frames": [],
              "notes": []
            }
            """)
        });
        var client = CreateClient(handler, new AiServiceOptions
        {
            BaseUrl = "http://localhost:8000",
            ApiKey = "internal-secret"
        });

        await client.AnalyzeFramesAsync(CreateRequest());

        IEnumerable<string>? values = null;
        var hasHeader = handler.LastRequest?.Headers.TryGetValues("X-AI-Service-Key", out values) == true;
        Assert.True(hasHeader);
        Assert.Equal("internal-secret", Assert.Single(values!));
    }

    private static PythonAiInferenceClient CreateClient(HttpResponseMessage response)
    {
        return CreateClient(new StaticResponseHandler(response), new AiServiceOptions { BaseUrl = "http://localhost:8000" });
    }

    private static PythonAiInferenceClient CreateClient(StaticResponseHandler handler, AiServiceOptions options)
    {
        return new PythonAiInferenceClient(
            new FakeHttpClientFactory(new HttpClient(handler)),
            Options.Create(options),
            new FakeProviderRequestGate(),
            new InMemoryProviderCircuitBreaker(),
            NullLogger<PythonAiInferenceClient>.Instance);
    }

    private static PythonAiInferenceClient CreateClient(HttpMessageHandler handler, AiServiceOptions options)
    {
        return new PythonAiInferenceClient(
            new FakeHttpClientFactory(new HttpClient(handler)),
            Options.Create(options),
            new FakeProviderRequestGate(),
            new InMemoryProviderCircuitBreaker(),
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

    private sealed class FakeProviderRequestGate : IProviderRequestGate
    {
        public Task<IAsyncDisposable> EnterAsync(
            long videoId,
            long jobId,
            int? segmentIndex,
            int? segmentAttempt,
            string providerMode,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IAsyncDisposable>(new Releaser());
        }

        private sealed class Releaser : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class StaticResponseHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceResponseHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private int _index;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            var index = Math.Min(_index++, responses.Length - 1);
            return Task.FromResult(responses[index]);
        }
    }
}
