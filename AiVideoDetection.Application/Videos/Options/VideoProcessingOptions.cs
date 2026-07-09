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

    public int FrameImageQuality { get; set; } = 2;

    public string FrameOutputFormat { get; set; } = "jpg";
}
