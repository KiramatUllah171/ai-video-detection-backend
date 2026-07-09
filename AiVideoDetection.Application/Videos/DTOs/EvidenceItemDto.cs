namespace AiVideoDetection.Application.Videos.DTOs;

public class EvidenceItemDto
{
    public long Id { get; init; }

    public string Type { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal? ScoreImpact { get; init; }

    public decimal? TimestampSeconds { get; init; }

    public long? VideoFrameId { get; init; }
}
