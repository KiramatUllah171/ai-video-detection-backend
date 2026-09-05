namespace AiVideoDetection.Application.Subscriptions.Options;

public class SubscriptionSecurityOptions
{
    public const string SectionName = "SubscriptionSecurity";

    public string? HmacSecret { get; set; }

    public string DeviceCookieName { get; set; } = "sachai_device";

    public int DeviceCookieDays { get; set; } = 365;

    public bool RequireSecureDeviceCookieInDevelopment { get; set; }

    public string DeviceFingerprintHeaderName { get; set; } = "X-SachAI-Device-Fingerprint";
}
