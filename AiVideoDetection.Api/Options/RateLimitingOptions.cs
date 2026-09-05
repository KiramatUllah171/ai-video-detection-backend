namespace AiVideoDetection.Api.Options;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;

    public int LoginPermitLimit { get; init; } = 10;

    public int AuthSensitivePermitLimit { get; init; } = 20;

    public int RefreshPermitLimit { get; init; } = 60;

    public int UploadPermitLimit { get; init; } = 10;

    public int VideoActionPermitLimit { get; init; } = 30;

    public int SubscriptionPermitLimit { get; init; } = 120;

    public int PaymentPermitLimit { get; init; } = 20;

    public int PaymentCallbackPermitLimit { get; init; } = 60;

    public int ReportDownloadPermitLimit { get; init; } = 30;

    public int AdminPermitLimit { get; init; } = 120;

    public int GeneralApiPermitLimit { get; init; } = 300;

    public int WindowSeconds { get; init; } = 60;
}
