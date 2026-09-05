using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Videos.StorageProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class VideoWorkloadGateTests
{
    [Fact]
    public async Task TryEnterUploadAsyncReturnsNullWhenUploadLimitIsReached()
    {
        var gate = CreateGate(maxConcurrentUploads: 1, maxConcurrentProcessingJobs: 1, uploadWaitSeconds: 0);
        var first = await gate.TryEnterUploadAsync();
        Assert.NotNull(first);

        try
        {
            var second = await gate.TryEnterUploadAsync();

            Assert.Null(second);
        }
        finally
        {
            await first!.DisposeAsync();
        }
    }

    [Fact]
    public async Task EnterProcessingAsyncSerializesProcessingWhenLimitIsOne()
    {
        var gate = CreateGate(maxConcurrentUploads: 1, maxConcurrentProcessingJobs: 1, uploadWaitSeconds: 0);
        var first = await gate.EnterProcessingAsync();
        IAsyncDisposable? second = null;

        try
        {
            var secondTask = gate.EnterProcessingAsync();
            await Task.Delay(100);
            Assert.False(secondTask.IsCompleted);

            await first.DisposeAsync();
            second = await secondTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.NotNull(second);
        }
        finally
        {
            if (second is not null)
            {
                await second.DisposeAsync();
            }
        }
    }

    private static VideoWorkloadGate CreateGate(
        int maxConcurrentUploads,
        int maxConcurrentProcessingJobs,
        int uploadWaitSeconds)
    {
        return new VideoWorkloadGate(
            Options.Create(new VideoStorageProtectionOptions
            {
                MaxConcurrentUploads = maxConcurrentUploads,
                MaxConcurrentProcessingJobs = maxConcurrentProcessingJobs,
                UploadConcurrencyWaitTimeoutSeconds = uploadWaitSeconds
            }),
            NullLogger<VideoWorkloadGate>.Instance);
    }
}
