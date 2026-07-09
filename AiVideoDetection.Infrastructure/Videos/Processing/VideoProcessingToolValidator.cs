using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class VideoProcessingToolValidator(
    IFfmpegToolLocator toolLocator,
    IProcessRunner processRunner,
    IOptions<VideoProcessingOptions> options,
    ILogger<VideoProcessingToolValidator> logger) : IVideoProcessingToolValidator
{
    private readonly VideoProcessingOptions _options = options.Value;

    public async Task<VideoProcessingToolCheckResult> ValidateAsync(CancellationToken cancellationToken = default)
    {
        var ffmpeg = await CheckToolAsync("ffmpeg", _options.FfmpegPath, cancellationToken);
        var ffprobe = await CheckToolAsync("ffprobe", _options.FfprobePath, cancellationToken);

        if (ffmpeg.Available && ffprobe.Available)
        {
            logger.LogInformation(
                "FFmpeg tools are available. ffmpeg: {FfmpegPath}; ffprobe: {FfprobePath}.",
                ffmpeg.ResolvedPath,
                ffprobe.ResolvedPath);
        }
        else
        {
            logger.LogWarning(
                "FFmpeg tool validation failed. ffmpeg available: {FfmpegAvailable}; ffprobe available: {FfprobeAvailable}.",
                ffmpeg.Available,
                ffprobe.Available);
        }

        return new VideoProcessingToolCheckResult(ffmpeg, ffprobe);
    }

    private async Task<ToolAvailabilityResult> CheckToolAsync(
        string toolName,
        string configuredValue,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolved = toolLocator.Resolve(toolName, configuredValue);
            var processResult = await processRunner.RunAsync(toolName, configuredValue, ["-version"], cancellationToken);
            var versionLine = processResult.StandardOutput
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();

            if (!processResult.Succeeded)
            {
                return new ToolAvailabilityResult(
                    toolName,
                    configuredValue,
                    resolved.ExecutablePath,
                    Available: false,
                    VersionFirstLine: versionLine,
                    ErrorCode: "PROCESS_START_FAILED",
                    ErrorMessage: $"{toolName} -version returned exit code {processResult.ExitCode}.");
            }

            return new ToolAvailabilityResult(
                toolName,
                configuredValue,
                resolved.ExecutablePath,
                Available: true,
                VersionFirstLine: versionLine,
                ErrorCode: null,
                ErrorMessage: null);
        }
        catch (ProcessingException exception)
        {
            return new ToolAvailabilityResult(
                toolName,
                configuredValue,
                ResolvedPath: null,
                Available: false,
                VersionFirstLine: null,
                ErrorCode: exception.ErrorCode,
                ErrorMessage: exception.SafeMessage);
        }
    }
}
