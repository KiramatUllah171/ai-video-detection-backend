using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Domain.Entities;

public class EvidenceItem
{
    public long Id { get; set; }

    public long AiResultId { get; set; }

    public AiResult AiResult { get; set; } = null!;

    public long? VideoFrameId { get; set; }

    public VideoFrame? VideoFrame { get; set; }

    public EvidenceType Type { get; set; }

    public EvidenceSeverity Severity { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal? ScoreImpact { get; set; }

    public decimal? TimestampSeconds { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
