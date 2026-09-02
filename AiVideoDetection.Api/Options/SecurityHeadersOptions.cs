namespace AiVideoDetection.Api.Options;

public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    public bool Enabled { get; init; } = true;

    public bool EnableHsts { get; init; } = true;

    public int HstsMaxAgeDays { get; init; } = 365;

    public string ContentSecurityPolicy { get; init; } =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'; img-src 'self' data: blob:; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'";

    public string PermissionsPolicy { get; init; } =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
}
