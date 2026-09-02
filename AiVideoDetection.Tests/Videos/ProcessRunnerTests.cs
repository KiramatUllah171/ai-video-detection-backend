using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using AiVideoDetection.Infrastructure.Videos.Processing;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class ProcessRunnerTests
{
    [Fact]
    public async Task MissingToolIncludesToolNameAndConfiguredValue()
    {
        var runner = new ProcessRunner(new ThrowingToolLocator(), Options.Create(new VideoProcessingOptions()));

        var exception = await Assert.ThrowsAsync<ProcessingException>(
            () => runner.RunAsync("ffprobe", "missing-ffprobe", ["-version"], CancellationToken.None));

        Assert.Equal("FFPROBE_NOT_FOUND", exception.ErrorCode);
        Assert.Contains("ffprobe", exception.SafeMessage);
        Assert.Contains("missing-ffprobe", exception.SafeMessage);
    }

    private sealed class ThrowingToolLocator : IFfmpegToolLocator
    {
        public FfmpegToolResolution Resolve(string toolName, string configuredValue)
        {
            throw new ProcessingException(
                "FFPROBE_NOT_FOUND",
                $"Unable to start {toolName}. Configured value: {configuredValue}. The process could not resolve this tool from PATH.");
        }
    }
}
