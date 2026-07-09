namespace AiVideoDetection.Application.Videos.Ai;

public sealed record AiAnalyzeFramesRequest(
    long VideoId,
    long JobId,
    IReadOnlyList<AiAnalyzeFrameItem> Frames);
