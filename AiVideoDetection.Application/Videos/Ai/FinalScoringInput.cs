namespace AiVideoDetection.Application.Videos.Ai;

public sealed record FinalScoringInput(
    decimal VisualScore,
    decimal AiConfidence,
    IReadOnlyList<string> MetadataWarnings,
    int FrameCount,
    decimal? TemporalScore,
    string? LabelHint,
    bool ModelDisagreement = false,
    decimal? VideoComponentScore = null,
    decimal? FrameRawScore = null,
    decimal? FrameCalibratedScore = null,
    bool StrongFrameEvidence = false,
    decimal? VideoConfidence = null,
    decimal? FrameConfidence = null,
    decimal? DetectorReliabilityScore = null);
