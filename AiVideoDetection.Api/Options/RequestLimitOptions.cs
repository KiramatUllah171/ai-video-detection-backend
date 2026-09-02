namespace AiVideoDetection.Api.Options;

public sealed class RequestLimitOptions
{
    public const string SectionName = "RequestLimits";

    public long MaxUploadBodySizeBytes { get; init; } = 524_288_000;

    public long MaxApiBodySizeBytes { get; init; } = 1_048_576;
}
