namespace AiVideoDetection.Application.Videos.Ai;

public sealed record AiAnalyzeFrameItem(
    long FrameId,
    string FrameUrl,
    int FrameIndex,
    decimal? TimestampSeconds,
    string? ImageBase64 = null);
