namespace AiVideoDetection.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string Secret { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int? RefreshTokenMinutes { get; set; }

    public int RefreshTokenDays { get; set; } = 7;

    public TimeSpan GetRefreshTokenLifetime()
    {
        return RefreshTokenMinutes.HasValue
            ? TimeSpan.FromMinutes(Math.Max(1, RefreshTokenMinutes.Value))
            : TimeSpan.FromDays(Math.Max(1, RefreshTokenDays));
    }
}
