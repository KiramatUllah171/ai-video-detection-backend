namespace AiVideoDetection.Application.Videos.DTOs;

public class AnalysisResultDto
{
    public long VideoId { get; init; }

    public long AiResultId { get; init; }

    public string? ModelVersion { get; init; }

    public string? ModelId { get; init; }

    public string? ModelCapability { get; init; }

    public bool IsMock { get; init; }

    public decimal AiGeneratedProbability { get; init; }

    public decimal LikelyRealProbability { get; init; }

    public decimal ConfidencePercentage { get; init; }

    public decimal VisualScore { get; init; }

    public decimal? MetadataScore { get; init; }

    public decimal? TemporalScore { get; init; }

    public decimal FinalScore { get; init; }

    public decimal Confidence { get; init; }

    public string Label { get; init; } = string.Empty;

    public string? Summary { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool ModelDisagreement { get; init; }

    public bool StrongFrameEvidence { get; init; }

    public decimal? MinimumRecommendedScore { get; init; }

    public string? EnsembleStrategy { get; init; }

    public string? ComponentScoresJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<EvidenceItemDto> EvidenceItems { get; init; } = [];
}
