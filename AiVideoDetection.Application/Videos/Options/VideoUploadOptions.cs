namespace AiVideoDetection.Application.Videos.Options;

public class VideoUploadOptions
{
    public const string SectionName = "VideoUpload";

    public long MaxFileSizeBytes { get; set; } = 524_288_000;

    public long UploadChunkSizeBytes { get; set; } = 15_728_640;

    public string[] AllowedExtensions { get; set; } = [".mp4", ".mov", ".avi", ".mkv", ".webm"];

    public string[] AllowedContentTypes { get; set; } =
    [
        "video/mp4",
        "video/quicktime",
        "video/x-msvideo",
        "video/avi",
        "video/msvideo",
        "video/x-matroska",
        "video/matroska",
        "video/mkv",
        "video/x-mkv",
        "application/x-matroska",
        "video/webm",
        "application/octet-stream"
    ];

    public int MaxDurationSeconds { get; set; } = 10_800;
}
