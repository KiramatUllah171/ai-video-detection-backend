namespace AiVideoDetection.Application.Videos.Options;

public static class VideoUploadSizeLimits
{
    public const long FreeMaxVideoSizeBytes = 209_715_200;
    public const long PlusMaxVideoSizeBytes = 262_144_000;
    public const long ProMaxVideoSizeBytes = 314_572_800;
    public const long AbsoluteMaxVideoSizeBytes = ProMaxVideoSizeBytes;
    public const long MultipartRequestBodyLimitBytes = 335_544_320;
}
