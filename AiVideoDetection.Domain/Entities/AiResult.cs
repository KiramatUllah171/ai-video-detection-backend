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

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<EvidenceItem> EvidenceItems { get; set; } = [];
}
