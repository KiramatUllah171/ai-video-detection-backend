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

    public string Provider { get; init; } = "Local";

    public string ProviderMode { get; init; } = "local";

    public string FinalDecisionSource { get; init; } = "Local";

    public string? ExternalProviderName { get; init; }

    public string? ExternalProviderStatus { get; init; }

    public decimal? ExternalScore { get; init; }

    public decimal? ExternalConfidence { get; init; }

    public string? ExternalLabel { get; init; }

    public bool FallbackUsed { get; init; }

    public string? FallbackReason { get; init; }

    public IReadOnlyList<string> ProviderWarnings { get; init; } = [];

    public string? LocalAnalysisSummary { get; init; }

    public string? ExternalAnalysisSummary { get; init; }

    public string? HybridDecisionSummary { get; init; }

    public DateTimeOffset? ProviderRequestedAt { get; init; }

    public DateTimeOffset? ProviderCompletedAt { get; init; }

    public bool ModelDisagreement { get; init; }

    public bool StrongFrameEvidence { get; init; }

    public decimal? MinimumRecommendedScore { get; init; }

    public string? EnsembleStrategy { get; init; }

    public string? ComponentScoresJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<EvidenceItemDto> EvidenceItems { get; init; } = [];
}
