using System.Diagnostics;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.StorageProtection;

public sealed class VideoWorkloadGate : IVideoWorkloadGate, IDisposable
{
    private readonly SemaphoreSlim _uploadSemaphore;
    private readonly SemaphoreSlim _processingSemaphore;
    private readonly TimeSpan _uploadWaitTimeout;
    private readonly ILogger<VideoWorkloadGate> _logger;

    public VideoWorkloadGate(
        IOptions<VideoStorageProtectionOptions> options,
        ILogger<VideoWorkloadGate> logger)
    {
        var uploadConcurrency = Math.Clamp(options.Value.MaxConcurrentUploads, 1, 100);
        var processingConcurrency = Math.Clamp(options.Value.MaxConcurrentProcessingJobs, 1, 32);
        _uploadSemaphore = new SemaphoreSlim(uploadConcurrency, uploadConcurrency);
        _processingSemaphore = new SemaphoreSlim(processingConcurrency, processingConcurrency);
        _uploadWaitTimeout = options.Value.UploadConcurrencyWaitTimeout;
        _logger = logger;
    }

    public async Task<IAsyncDisposable?> TryEnterUploadAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var entered = await _uploadSemaphore.WaitAsync(_uploadWaitTimeout, cancellationToken);
        if (!entered)
        {
            _logger.LogWarning("Video upload concurrency limit reached.");
            return null;
        }

        LogWaitIfUseful("upload", started);
        return new Releaser(_uploadSemaphore);
    }

    public async Task<IAsyncDisposable> EnterProcessingAsync(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        await _processingSemaphore.WaitAsync(cancellationToken);
        LogWaitIfUseful("processing", started);
        return new Releaser(_processingSemaphore);
    }

    public void Dispose()
    {
        _uploadSemaphore.Dispose();
        _processingSemaphore.Dispose();
    }

    private void LogWaitIfUseful(string workload, long started)
    {
        var waitMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (waitMs > 100)
        {
            _logger.LogInformation(
                "Video {Workload} waited {WaitMilliseconds} ms for local concurrency slot.",
                workload,
                Math.Round(waitMs));
        }
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
