namespace AiVideoDetection.Application.Videos.Ai;

public sealed record AiAnalyzeFramesResponse(
    long VideoId,
    long JobId,
    string ModelVersion,
    decimal OverallAiScore,
    decimal OverallConfidence,
    string LabelHint,
    IReadOnlyList<AiFrameAnalysisResult> Frames,
    IReadOnlyList<string> Notes)
{
    public string RawJson { get; init; } = "{}";
}
