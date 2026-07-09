namespace AiVideoDetection.Application.Videos.Ai;

public sealed record FinalScoringInput(
    decimal VisualScore,
    decimal AiConfidence,
    IReadOnlyList<string> MetadataWarnings,
    int FrameCount,
    decimal? TemporalScore,
    string? LabelHint);
