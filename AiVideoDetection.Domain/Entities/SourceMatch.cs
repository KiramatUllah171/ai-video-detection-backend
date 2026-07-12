using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Domain.Entities;

public class SourceMatch
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public Video Video { get; set; } = null!;

    public string Platform { get; set; } = string.Empty;

    public string? Url { get; set; }

    public string? Title { get; set; }

    public string? UploaderName { get; set; }

    public DateTimeOffset? UploadDatetime { get; set; }

    public decimal SimilarityScore { get; set; }

    public decimal? DurationMatchScore { get; set; }

    public decimal? HashMatchScore { get; set; }

    public decimal? MetadataMatchScore { get; set; }

    public decimal? SourceCredibilityScore { get; set; }

    public int Rank { get; set; } = 1;

    public ConfidenceLevel Confidence { get; set; } = ConfidenceLevel.Medium;

    public string? DetailsJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
