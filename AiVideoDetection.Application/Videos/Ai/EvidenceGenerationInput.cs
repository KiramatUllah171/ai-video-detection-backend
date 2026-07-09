namespace AiVideoDetection.Application.Videos.Ai;

public sealed record EvidenceGenerationInput(
    string ModelVersion,
    IReadOnlyList<AiFrameAnalysisResult> FrameResults,
    IReadOnlyDictionary<long, long> FrameIdToVideoFrameId,
    IReadOnlyList<string> MetadataWarnings,
    FinalScoringResult ScoringResult);
