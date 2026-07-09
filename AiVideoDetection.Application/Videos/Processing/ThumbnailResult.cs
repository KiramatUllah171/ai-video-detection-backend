namespace AiVideoDetection.Application.Videos.Processing;

public sealed record ThumbnailResult(
    string FilePath,
    decimal TimestampSeconds,
    int? Width,
    int? Height);
