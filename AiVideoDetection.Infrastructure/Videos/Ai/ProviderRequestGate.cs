using System.Diagnostics;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Ai;

public sealed class ProviderRequestGate : IProviderRequestGate, IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger<ProviderRequestGate> _logger;

    public ProviderRequestGate(
        IOptions<VideoProcessingOptions> options,
        ILogger<ProviderRequestGate> logger)
    {
        var concurrency = Math.Clamp(options.Value.MaxConcurrentProviderRequests, 1, 32);
        _semaphore = new SemaphoreSlim(concurrency, concurrency);
        _logger = logger;
    }

    public async Task<IAsyncDisposable> EnterAsync(
        long videoId,
        long jobId,
        int? segmentIndex,
        int? segmentAttempt,
        string providerMode,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        await _semaphore.WaitAsync(cancellationToken);
        var waitMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (waitMs > 100)
        {
            _logger.LogInformation(
                "Provider request for video {VideoId} job {JobId} segment {SegmentIndex} attempt {SegmentAttempt} waited {WaitMilliseconds} ms for concurrency slot using mode {ProviderMode}.",
                videoId,
                jobId,
                segmentIndex,
                segmentAttempt,
                Math.Round(waitMs),
                providerMode);
        }

        return new Releaser(_semaphore);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}
