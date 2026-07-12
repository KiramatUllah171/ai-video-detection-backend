namespace AiVideoDetection.Application.Videos.Options;

public class ScoringOptions
{
    public const string SectionName = "Scoring";

    public decimal VisualWeight { get; set; } = 0.90m;

    public decimal MetadataWeight { get; set; } = 0.10m;

    public decimal TemporalWeight { get; set; } = 0.00m;

    public decimal VideoDetectorMaxWeight { get; set; } = 0.65m;

    public decimal FrameDetectorMaxWeight { get; set; } = 0.35m;

    public bool NormalizeMissingWeights { get; set; } = true;

    public decimal MeanFrameScoreWeight { get; set; } = 0.45m;

    public decimal TopKFrameScoreWeight { get; set; } = 0.35m;

    public decimal P90FrameScoreWeight { get; set; } = 0.20m;

    public decimal LikelyAiThreshold { get; set; } = 0.70m;

    public decimal SuspiciousThreshold { get; set; } = 0.50m;

    public decimal InconclusiveThreshold { get; set; } = 0.30m;

    public decimal MinimumConfidenceForStrongLabel { get; set; } = 0.60m;

    public decimal HighFrameScoreThreshold { get; set; } = 0.75m;

    public decimal MediumFrameScoreThreshold { get; set; } = 0.55m;

    public decimal ModelDisagreementThreshold { get; set; } = 0.30m;

    public decimal StrongRawFrameThreshold { get; set; } = 0.85m;

    public decimal StrongCalibratedFrameThreshold { get; set; } = 0.65m;

    public bool RequireAgreementForLikelyAi { get; set; } = true;

    public bool AllowSingleDetectorSuspicious { get; set; } = true;

    public decimal MinimumDetectorReliabilityForSingleDetectorSuspicious { get; set; } = 0.50m;

    public int MaxFrameEvidenceItems { get; set; } = 10;
}
