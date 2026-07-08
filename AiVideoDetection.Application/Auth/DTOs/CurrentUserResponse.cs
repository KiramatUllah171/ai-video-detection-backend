using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Application.Auth.DTOs;

public class CurrentUserResponse
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public UserRole Role { get; set; }
}
