using AiVideoDetection.Infrastructure.Videos.Processing;

namespace AiVideoDetection.Tests.Videos;

public class FfmpegToolLocatorTests
{
    [Fact]
    public void ResolveUsesExistingAbsolutePathDirectly()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "ai-video-tool-tests", Guid.NewGuid().ToString("N"), "ffprobe.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, string.Empty);
        var locator = new FfmpegToolLocator();

        var result = locator.Resolve("ffprobe", filePath);

        Assert.Equal(filePath, result.ExecutablePath);
        Assert.False(result.ResolvedFromPath);
    }

    [Fact]
    public void MissingAbsolutePathThrowsFfprobeNotFound()
    {
        var locator = new FfmpegToolLocator();
        var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "ffprobe.exe");

        var exception = Assert.Throws<ProcessingException>(() => locator.Resolve("ffprobe", missingPath));

        Assert.Equal("FFPROBE_NOT_FOUND", exception.ErrorCode);
        Assert.Contains("configured absolute path does not exist", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(missingPath, exception.SafeMessage);
    }

    [Fact]
    public void MissingCommandNameThrowsToolSpecificNotFound()
    {
        var locator = new FfmpegToolLocator();
        var commandName = $"missing-tool-{Guid.NewGuid():N}";

        var exception = Assert.Throws<ProcessingException>(() => locator.Resolve("ffmpeg", commandName));

        Assert.Equal("FFMPEG_NOT_FOUND", exception.ErrorCode);
        Assert.Contains("could not resolve this tool from PATH", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("VideoProcessing:FfmpegPath", exception.SafeMessage);
    }

    [Fact]
    public void CommandNameCanResolveFromPathWithPlatformExtension()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ai-video-tool-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executablePath = Path.Combine(directory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        File.WriteAllText(executablePath, string.Empty);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        Environment.SetEnvironmentVariable("PATH", $"{directory}{Path.PathSeparator}{originalPath}");

        try
        {
            var locator = new FfmpegToolLocator();

            var result = locator.Resolve("ffprobe", "ffprobe");

            Assert.Equal(executablePath, result.ExecutablePath);
            Assert.True(result.ResolvedFromPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
        }
    }

    [Fact]
    public void CommandNameDoesNotFailWhenWindowsAppPathsAreUnavailable()
    {
        var locator = new FfmpegToolLocator();
        var commandName = $"missing-app-path-tool-{Guid.NewGuid():N}";

        var exception = Assert.Throws<ProcessingException>(() => locator.Resolve("ffprobe", commandName));

        Assert.Equal("FFPROBE_NOT_FOUND", exception.ErrorCode);
    }
}
