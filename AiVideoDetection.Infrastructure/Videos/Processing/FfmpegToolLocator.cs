using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Processing;
using Microsoft.Win32;
using System.Runtime.Versioning;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class FfmpegToolLocator : IFfmpegToolLocator
{
    public FfmpegToolResolution Resolve(string toolName, string configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            throw NotFound(toolName, configuredValue, "No configured path value was provided.");
        }

        var normalized = Environment.ExpandEnvironmentVariables(configuredValue.Trim().Trim('"'));

        if (Path.IsPathFullyQualified(normalized))
        {
            if (File.Exists(normalized))
            {
                return new FfmpegToolResolution(toolName, configuredValue, normalized, ResolvedFromPath: false);
            }

            throw NotFound(
                toolName,
                configuredValue,
                $"The configured absolute path does not exist: {normalized}");
        }

        if (LooksLikePath(normalized))
        {
            var fullPath = Path.GetFullPath(normalized);
            if (File.Exists(fullPath))
            {
                return new FfmpegToolResolution(toolName, configuredValue, fullPath, ResolvedFromPath: false);
            }

            throw NotFound(
                toolName,
                configuredValue,
                $"The configured relative path does not exist: {normalized}");
        }

        var resolved = ResolveCommandFromPath(normalized);
        if (resolved is not null)
        {
            return new FfmpegToolResolution(toolName, configuredValue, resolved, ResolvedFromPath: true);
        }

        resolved = ResolveCommandFromWindowsAppPaths(normalized);
        if (resolved is not null)
        {
            return new FfmpegToolResolution(toolName, configuredValue, resolved, ResolvedFromPath: true);
        }

        throw NotFound(
            toolName,
            configuredValue,
            $"The process could not resolve this tool from PATH. Set VideoProcessing:{GetConfigKey(toolName)} to the absolute {toolName}.exe path.");
    }

    private static string? ResolveCommandFromPath(string commandName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        var candidateNames = GetCandidateCommandNames(commandName);
        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidateName in candidateNames)
            {
                try
                {
                    var candidatePath = Path.GetFullPath(Path.Combine(directory, candidateName));
                    if (File.Exists(candidatePath))
                    {
                        return candidatePath;
                    }
                }
                catch
                {
                    // Ignore malformed PATH entries and continue searching.
                }
            }
        }

        return null;
    }

    private static string? ResolveCommandFromWindowsAppPaths(string commandName)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        foreach (var candidateName in GetCandidateCommandNames(commandName))
        {
            var registryValue = TryReadAppPath(Registry.CurrentUser, candidateName)
                ?? TryReadAppPath(Registry.LocalMachine, candidateName);

            if (!string.IsNullOrWhiteSpace(registryValue) && File.Exists(registryValue))
            {
                return registryValue;
            }
        }

        return null;
    }

    [SupportedOSPlatform("windows")]
    private static string? TryReadAppPath(RegistryKey rootKey, string executableName)
    {
        try
        {
            using var key = rootKey.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
            return key?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> GetCandidateCommandNames(string commandName)
    {
        if (!OperatingSystem.IsWindows() || Path.HasExtension(commandName))
        {
            return [commandName];
        }

        var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
        var extensions = string.IsNullOrWhiteSpace(pathExt)
            ? [".exe", ".cmd", ".bat"]
            : pathExt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return extensions
            .Prepend(".exe")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(extension => $"{commandName}{extension.ToLowerInvariant()}")
            .Prepend(commandName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool LooksLikePath(string value)
    {
        return value.Contains(Path.DirectorySeparatorChar)
            || value.Contains(Path.AltDirectorySeparatorChar);
    }

    private static ProcessingException NotFound(string toolName, string configuredValue, string details)
    {
        var safeConfiguredValue = string.IsNullOrWhiteSpace(configuredValue) ? "<empty>" : configuredValue;
        return new ProcessingException(
            GetNotFoundErrorCode(toolName),
            $"Unable to start {toolName}. Configured value: {safeConfiguredValue}. {details}");
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
}
