namespace AiVideoDetection.Application.Videos.DTOs;

public class SourceMatchDto
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public string Platform { get; set; } = string.Empty;

    public string? Title { get; set; }

    public DateTimeOffset? UploadDatetime { get; set; }

    public decimal SimilarityScore { get; set; }

    public decimal? DurationMatchScore { get; set; }

    public decimal? HashMatchScore { get; set; }

    public decimal? MetadataMatchScore { get; set; }

    public int Rank { get; set; }

    public string Confidence { get; set; } = string.Empty;

    public string? Details { get; set; }
}
