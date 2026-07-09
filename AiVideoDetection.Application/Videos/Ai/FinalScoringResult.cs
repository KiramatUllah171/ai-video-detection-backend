using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Application.Videos.Ai;

public sealed record FinalScoringResult(
    decimal VisualScore,
    decimal? MetadataScore,
    decimal? TemporalScore,
    decimal FinalScore,
    decimal Confidence,
    AnalysisLabel Label,
    string Summary);
