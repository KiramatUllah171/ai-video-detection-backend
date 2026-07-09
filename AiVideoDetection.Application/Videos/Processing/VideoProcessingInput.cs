namespace AiVideoDetection.Application.Videos.Processing;

public sealed record VideoProcessingInput(
    long JobId,
    long VideoId,
    string SourceFilePath,
    string WorkingDirectory,
    decimal? DurationSeconds);
