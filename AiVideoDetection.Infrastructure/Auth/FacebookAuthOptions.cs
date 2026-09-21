namespace AiVideoDetection.Infrastructure.Auth;

public class FacebookAuthOptions
{
    public const string SectionName = "FacebookAuth";

    public string AppId { get; set; } = string.Empty;

    public string AppSecret { get; set; } = string.Empty;

    public string TokenEndpoint { get; set; } = "https://graph.facebook.com/v20.0/oauth/access_token";

    public string UserInfoEndpoint { get; set; } = "https://graph.facebook.com/v20.0/me";
}
