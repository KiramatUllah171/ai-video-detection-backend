namespace AiVideoDetection.Application.Videos.Processing;

public sealed record FfmpegToolResolution(
    string ToolName,
    string ConfiguredValue,
    string ExecutablePath,
    bool ResolvedFromPath);
