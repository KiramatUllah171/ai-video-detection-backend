namespace AiVideoDetection.Application.Videos.Matching;

public sealed record FrameHashInput(
    long FrameId,
    long VideoId,
    string FrameUrl,
    int FrameIndex,
    decimal TimestampSeconds,
    string HashVersion,
    string? SourceVideoHash = null);
