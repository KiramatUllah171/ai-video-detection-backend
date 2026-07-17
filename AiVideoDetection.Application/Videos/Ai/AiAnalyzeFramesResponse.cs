namespace AiVideoDetection.Application.Videos.Ai;

public sealed record AiAnalyzeFramesResponse(
    long VideoId,
    long JobId,
    string ModelId,
    string ModelVersion,
    string ModelCapability,
    bool IsMock,
    decimal OverallAiScore,
    decimal RealProbability,
    decimal OverallConfidence,
    string LabelHint,
    IReadOnlyList<AiFrameAnalysisResult> Frames,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Warnings,
    bool ModelDisagreement = false,
    bool StrongFrameEvidence = false,
    decimal? MinimumRecommendedScore = null,
    string? EnsembleStrategy = null,
    string? ComponentScoresJson = null,
    decimal? VideoComponentScore = null,
    decimal? FrameComponentScore = null,
    decimal? FrameRawScore = null,
    decimal? FrameCalibratedScore = null,
    decimal? FrameReliabilityScore = null,
    string Provider = "Local",
    string ProviderMode = "local",
    string? ExternalProviderResultJson = null,
    bool FallbackUsed = false,
    string? FallbackReason = null,
    string? LocalResultJson = null,
    string? BitMindResultJson = null,
    string FinalDecisionSource = "Local")
{
    public string RawJson { get; init; } = "{}";
}
