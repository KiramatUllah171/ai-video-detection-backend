namespace AiVideoDetection.Application.Auth.DTOs;

public class FacebookLoginRequest
{
    public string Code { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;
}
