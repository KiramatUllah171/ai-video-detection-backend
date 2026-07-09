namespace AiVideoDetection.Application.Videos.Processing;

public sealed record ExtractedFrameResult(
    string FilePath,
    int FrameIndex,
    decimal TimestampSeconds,
    int? Width,
    int? Height,
    bool IsKeyframe);
