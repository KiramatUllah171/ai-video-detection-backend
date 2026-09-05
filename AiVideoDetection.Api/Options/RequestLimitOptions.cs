using AiVideoDetection.Application.Videos.Options;

namespace AiVideoDetection.Api.Options;

public sealed class RequestLimitOptions
{
    public const string SectionName = "RequestLimits";

    public long MaxUploadBodySizeBytes { get; init; } = VideoUploadSizeLimits.MultipartRequestBodyLimitBytes;

    public long MaxApiBodySizeBytes { get; init; } = 1_048_576;
}
