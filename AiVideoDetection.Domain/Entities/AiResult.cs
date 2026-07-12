using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Domain.Entities;

public class AiResult
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public Video Video { get; set; } = null!;

    public long? ModelVersionId { get; set; }

    public ModelVersion? ModelVersion { get; set; }

    public decimal VisualScore { get; set; }

    public decimal? TemporalScore { get; set; }

    public decimal? MetadataScore { get; set; }

    public decimal FinalScore { get; set; }

    public decimal Confidence { get; set; }

    public AnalysisLabel Label { get; set; }

    public string RawModelOutputJson { get; set; } = "{}";

    public string? Summary { get; set; }

    public string Provider { get; set; } = "Local";

    public string ProviderMode { get; set; } = "local";

    public string FinalDecisionSource { get; set; } = "Local";

    public string? ExternalProviderName { get; set; }

    public string? ExternalProviderResultId { get; set; }

    public string? ExternalProviderJobId { get; set; }

    public string? ExternalProviderStatus { get; set; }

    public decimal? ExternalScore { get; set; }

    public decimal? ExternalConfidence { get; set; }

    public string? ExternalLabel { get; set; }

    public string? ExternalRawResponseJson { get; set; }

    public string? ExternalErrorMessage { get; set; }

    public DateTimeOffset? ExternalRequestedAt { get; set; }

    public DateTimeOffset? ExternalCompletedAt { get; set; }

    public bool FallbackUsed { get; set; }

    public string? FallbackReason { get; set; }

    public string? LocalResultJson { get; set; }

    public string? HybridResultJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<EvidenceItem> EvidenceItems { get; set; } = [];

    public ICollection<AiProviderRequest> ProviderRequests { get; set; } = [];
}
