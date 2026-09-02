using System.Diagnostics;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class ProcessRunner(IFfmpegToolLocator toolLocator, IOptions<VideoProcessingOptions> options) : IProcessRunner
{
    private readonly VideoProcessingOptions _options = options.Value;

    public async Task<ProcessRunResult> RunAsync(
        string toolName,
        string configuredFileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var resolvedTool = toolLocator.Resolve(toolName, configuredFileName);
        var startInfo = new ProcessStartInfo
        {
            FileName = resolvedTool.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var timeout = GetTimeout(toolName);
        timeoutCts.CancelAfter(timeout);

        try
        {
            if (!process.Start())
            {
                throw new ProcessingException("PROCESS_START_FAILED", "Media processing tool could not be started.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ProcessingException(
                GetNotFoundErrorCode(toolName),
                $"Unable to start {toolName}. Configured value: {configuredFileName}. The resolved executable could not be started. Set VideoProcessing:{GetConfigKey(toolName)} to the absolute {toolName}.exe path.",
                exception);
        }
        catch (InvalidOperationException exception)
        {
            throw new ProcessingException(
                "PROCESS_START_FAILED",
                $"Unable to start {toolName}. The process start request was invalid.",
                exception);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            KillProcessTree(process);
            throw new ProcessingException(
                "PROCESS_TIMEOUT",
                $"{toolName} timed out after {Math.Ceiling(timeout.TotalSeconds)} seconds.",
                exception);
        }

        return new ProcessRunResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static string GetNotFoundErrorCode(string toolName)
    {
        return string.Equals(toolName, "ffprobe", StringComparison.OrdinalIgnoreCase)
            ? "FFPROBE_NOT_FOUND"
            : "FFMPEG_NOT_FOUND";
    }

    private static string GetConfigKey(string toolName)
    {
        return string.Equals(toolName, "ffprobe", StringComparison.OrdinalIgnoreCase)
            ? "FfprobePath"
            : "FfmpegPath";
    }

    private TimeSpan GetTimeout(string toolName)
    {
        return string.Equals(toolName, "ffprobe", StringComparison.OrdinalIgnoreCase)
            ? _options.FfprobeTimeout
            : _options.FfmpegTimeout;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }
}
