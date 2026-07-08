namespace AiVideoDetection.Application.Auth.DTOs;

public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string RefreshToken { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public UserDto User { get; set; } = new();
}
