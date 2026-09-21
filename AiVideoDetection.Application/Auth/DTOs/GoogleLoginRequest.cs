namespace AiVideoDetection.Application.Auth.DTOs;

public class GoogleLoginRequest
{
    public string Code { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;
}
