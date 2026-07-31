namespace AiVideoDetection.Application.Videos.Ai;

public sealed record AiAnalyzeVideoRequest(
    long VideoId,
    long JobId,
    long UserId,
    string ProviderMode,
    string OriginalVideoPath,
    IReadOnlyList<AiAnalyzeFrameItem> Frames,
    int? SegmentIndex = null,
    int? SegmentAttempt = null);
