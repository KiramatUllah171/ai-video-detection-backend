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

    [Fact]
    public async Task RunAsyncThrowsWhenProcessOutputExceedsLimit()
    {
        var command = CreateNoisyCommand();
        var runner = new ProcessRunner(
            new FakeToolLocator(command.ExecutablePath),
            Options.Create(new VideoProcessingOptions
            {
                MaxProcessOutputBytes = 128,
                FfmpegTimeoutSeconds = 10
            }));

        var exception = await Assert.ThrowsAsync<ProcessingException>(
            () => runner.RunAsync("ffmpeg", command.ExecutablePath, command.Arguments, CancellationToken.None));

        Assert.Equal("PROCESS_OUTPUT_LIMIT_EXCEEDED", exception.ErrorCode);
    }

    private static TestCommand CreateNoisyCommand()
    {
        if (OperatingSystem.IsWindows())
        {
            return new TestCommand(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                ["/c", "for /L %i in (1,1,2000) do @echo x"]);
        }

        return new TestCommand(
            "/bin/sh",
            ["-c", "yes x | head -n 2000"]);
    }

    private sealed record TestCommand(string ExecutablePath, IReadOnlyList<string> Arguments);

    private sealed class ThrowingToolLocator : IFfmpegToolLocator
    {
        public FfmpegToolResolution Resolve(string toolName, string configuredValue)
        {
            throw new ProcessingException(
                "FFPROBE_NOT_FOUND",
                $"Unable to start {toolName}. Configured value: {configuredValue}. The process could not resolve this tool from PATH.");
        }
    }

    private sealed class FakeToolLocator(string executablePath) : IFfmpegToolLocator
    {
        public FfmpegToolResolution Resolve(string toolName, string configuredValue)
        {
            return new FfmpegToolResolution(toolName, configuredValue, executablePath, ResolvedFromPath: false);
        }
    }
}
