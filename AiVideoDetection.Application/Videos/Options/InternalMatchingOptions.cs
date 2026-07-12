namespace AiVideoDetection.Application.Videos.Options;

public class InternalMatchingOptions
{
    public const string SectionName = "InternalMatching";

    public bool Enabled { get; set; } = true;

    public string HashVersion { get; set; } = "mvp-v1";

    public int MaxCandidateVideos { get; set; } = 200;

    public int MaxFramesPerVideoToCompare { get; set; } = 30;

    public int MaxMatchesToStore { get; set; } = 10;

    public int HammingDistanceThreshold { get; set; } = 18;

    public int MinimumMatchedFrames { get; set; } = 2;

    public decimal MinimumSimilarityScore { get; set; } = 0.55m;

    public decimal HighConfidenceThreshold { get; set; } = 0.80m;

    public decimal MediumConfidenceThreshold { get; set; } = 0.65m;

    public decimal HashSimilarityWeight { get; set; } = 0.70m;

    public decimal DurationSimilarityWeight { get; set; } = 0.20m;

    public decimal MetadataSimilarityWeight { get; set; } = 0.10m;

    public bool PlaceholderHashMode { get; set; } = true;

    public bool FailJobOnMatchingError { get; set; }
}
