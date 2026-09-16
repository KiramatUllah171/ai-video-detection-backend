namespace AiVideoDetection.Application.Videos.Options;

public class VideoProcessingOptions
{
    public const string SectionName = "VideoProcessing";

    public string FfmpegPath { get; set; } = "ffmpeg";

    public string FfprobePath { get; set; } = "ffprobe";

    public string WorkingRootPath { get; set; } = "storage/work";

    public decimal ThumbnailTimestampSeconds { get; set; } = 1;

    public decimal FrameIntervalSeconds { get; set; } = 2;

    public int MaxExtractedFrames { get; set; } = 30;

    public long MaxFrameFileSizeBytes { get; set; } = 8_388_608;

    public int MaxVideoWidth { get; set; } = 4096;

    public int MaxVideoHeight { get; set; } = 4096;

    public decimal MaxFramesPerSecond { get; set; } = 120;

    public int MaxVideoStreams { get; set; } = 2;

    public int MaxAudioStreams { get; set; } = 8;

    public int MaxSubtitleStreams { get; set; } = 16;

    public int MaxAttachmentStreams { get; set; } = 8;

    public int MaxTotalStreams { get; set; } = 32;

    public int MaxProcessOutputBytes { get; set; } = 1_048_576;

    public int FrameImageQuality { get; set; } = 2;

    public string FrameOutputFormat { get; set; } = "jpg";

    public decimal SmartScanClipDurationSeconds { get; set; } = 6;

    public int MaxSegmentCount { get; set; } = 20;

    public int SegmentConcurrency { get; set; } = 2;

    public int MaxConcurrentProviderRequests { get; set; } = 2;

    public int UserRetryLimit { get; set; } = 3;

    public int StaleActiveJobTimeoutMinutes { get; set; } = 30;

    public int FfmpegTimeoutSeconds { get; set; } = 600;

    public int FfprobeTimeoutSeconds { get; set; } = 120;

    public int WorkerTimeoutMinutes { get; set; } = 30;

    public decimal HighRiskOverrideScore { get; set; } = 0.90m;

    public decimal MinimumRequiredCoverageRatio { get; set; } = 0.80m;

    public int TemporaryFileRetentionHours { get; set; } = 24;

    public int OriginalVideoRetentionDays { get; set; } = 3;

    public int ReportRetentionDays { get; set; } = 3;

    public int DetailedResultRetentionDays { get; set; } = 3;

    public TimeSpan TemporaryFileRetention => TimeSpan.FromHours(TemporaryFileRetentionHours);

    public TimeSpan OriginalVideoRetention => TimeSpan.FromDays(OriginalVideoRetentionDays);

    public TimeSpan ReportRetention => TimeSpan.FromDays(ReportRetentionDays);

    public TimeSpan DetailedResultRetention => TimeSpan.FromDays(DetailedResultRetentionDays);

    public TimeSpan FfmpegTimeout => TimeSpan.FromSeconds(FfmpegTimeoutSeconds);

    public TimeSpan FfprobeTimeout => TimeSpan.FromSeconds(FfprobeTimeoutSeconds);

    public TimeSpan WorkerTimeout => TimeSpan.FromMinutes(WorkerTimeoutMinutes);

    public Dictionary<string, int> ProgressStageWeights { get; set; } = new()
    {
        ["Preparing"] = 20,
        ["SegmentPreparation"] = 30,
        ["Analysis"] = 40,
        ["Finalization"] = 10
    };

    public VideoSamplingTierOptions[] SmartScanTiers { get; set; } =
    [
        new() { MaxDurationSeconds = 6, SegmentCount = 1 },
        new() { MaxDurationSeconds = 30, SegmentCount = 1 },
        new() { MaxDurationSeconds = 60, SegmentCount = 3 },
        new() { MaxDurationSeconds = 300, SegmentCount = 5 },
        new() { MaxDurationSeconds = 900, SegmentCount = 7 },
        new() { MaxDurationSeconds = 1800, SegmentCount = 9 },
        new() { MaxDurationSeconds = 3600, SegmentCount = 12 },
        new() { MaxDurationSeconds = 5400, SegmentCount = 16 },
        new() { MaxDurationSeconds = decimal.MaxValue, SegmentCount = 20 }
    ];

    public VideoSamplingTierOptions[] DetailedScanTiers { get; set; } =
    [
        new() { MaxDurationSeconds = 30, SegmentCount = 1 },
        new() { MaxDurationSeconds = 60, SegmentCount = 5 },
        new() { MaxDurationSeconds = 300, SegmentCount = 8 },
        new() { MaxDurationSeconds = 900, SegmentCount = 12 },
        new() { MaxDurationSeconds = 1800, SegmentCount = 16 },
        new() { MaxDurationSeconds = 3600, SegmentCount = 20 },
        new() { MaxDurationSeconds = 5400, SegmentCount = 24 },
        new() { MaxDurationSeconds = decimal.MaxValue, SegmentCount = 30 }
    ];
}
