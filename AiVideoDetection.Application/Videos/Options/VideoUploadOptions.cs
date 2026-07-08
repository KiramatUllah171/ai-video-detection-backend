namespace AiVideoDetection.Application.Videos.Options;

public class VideoUploadOptions
{
    public const string SectionName = "VideoUpload";

    public long MaxFileSizeBytes { get; set; } = 524_288_000;

    public string[] AllowedExtensions { get; set; } = [".mp4", ".mov", ".avi", ".mkv", ".webm"];

    public string[] AllowedContentTypes { get; set; } =
    [
        "video/mp4",
        "video/quicktime",
        "video/x-msvideo",
        "video/x-matroska",
        "video/webm",
        "application/octet-stream"
    ];

    public int MaxDurationSeconds { get; set; } = 3600;
}
