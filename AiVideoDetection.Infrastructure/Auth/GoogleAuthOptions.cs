namespace AiVideoDetection.Infrastructure.Auth;

public class GoogleAuthOptions
{
    public const string SectionName = "GoogleAuth";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";

    public string UserInfoEndpoint { get; set; } = "https://www.googleapis.com/oauth2/v3/userinfo";
}
