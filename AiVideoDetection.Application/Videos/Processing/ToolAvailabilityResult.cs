namespace AiVideoDetection.Application.Videos.Processing;

public sealed record ToolAvailabilityResult(
    string ToolName,
    string ConfiguredValue,
    string? ResolvedPath,
    bool Available,
    string? VersionFirstLine,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record VideoProcessingToolCheckResult(
    ToolAvailabilityResult Ffmpeg,
    ToolAvailabilityResult Ffprobe);
