namespace AiVideoDetection.Infrastructure.Storage;

public sealed class R2Options
{
    public const string SectionName = "R2";

    public string? AccountId { get; set; }

    public string? ServiceUrl { get; set; }

    public string BucketName { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string Region { get; set; } = "auto";

    public string? PublicBaseUrl { get; set; }
}
