namespace AiVideoDetection.Application.Videos.Ai;

public sealed record AiFrameAnalysisResult(
    long FrameId,
    int FrameIndex,
    decimal? TimestampSeconds,
    decimal AiScore,
    decimal Confidence,
    IReadOnlyList<string> Notes);
