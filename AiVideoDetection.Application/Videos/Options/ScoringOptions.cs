namespace AiVideoDetection.Application.Videos.Options;

public class ScoringOptions
{
    public const string SectionName = "Scoring";

    public decimal VisualWeight { get; set; } = 0.70m;

    public decimal MetadataWeight { get; set; } = 0.20m;

    public decimal TemporalWeight { get; set; } = 0.10m;

    public decimal LikelyAiThreshold { get; set; } = 0.75m;

    public decimal SuspiciousThreshold { get; set; } = 0.55m;

    public decimal InconclusiveThreshold { get; set; } = 0.35m;

    public decimal MinimumConfidenceForStrongLabel { get; set; } = 0.65m;

    public decimal HighFrameScoreThreshold { get; set; } = 0.75m;

    public decimal MediumFrameScoreThreshold { get; set; } = 0.55m;

    public int MaxFrameEvidenceItems { get; set; } = 10;
}
