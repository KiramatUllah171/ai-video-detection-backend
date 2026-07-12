namespace AiVideoDetection.Application.Videos.Matching;

public sealed record FrameHashResult(
    long FrameId,
    long VideoId,
    string? PHash,
    string? DHash,
    string? AHash,
    string HashVersion);
