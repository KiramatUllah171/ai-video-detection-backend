namespace AiVideoDetection.Application.Videos.DTOs;

public class AnalysisResultDto
{
    public long VideoId { get; init; }

    public long AiResultId { get; init; }

    public string? ModelVersion { get; init; }

    public decimal VisualScore { get; init; }

    public decimal? MetadataScore { get; init; }

    public decimal? TemporalScore { get; init; }

    public decimal FinalScore { get; init; }

    public decimal Confidence { get; init; }

    public string Label { get; init; } = string.Empty;

    public string? Summary { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<EvidenceItemDto> EvidenceItems { get; init; } = [];
}
