namespace AiVideoDetection.Application.Videos.Ai;

public sealed record AiFrameAnalysisResult(
    long FrameId,
    int FrameIndex,
    decimal? TimestampSeconds,
    decimal AiScore,
    decimal RealProbability,
    decimal Confidence,
    IReadOnlyList<string> Notes);
