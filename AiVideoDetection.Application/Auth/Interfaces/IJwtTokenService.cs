using AiVideoDetection.Domain.Entities;

namespace AiVideoDetection.Application.Auth.Interfaces;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) GenerateAccessToken(User user);
}
