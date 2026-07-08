using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Auth;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Auth;

public class JwtTokenServiceTests
{
    [Fact]
    public void GenerateAccessToken_CreatesTokenWithRoleClaim()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Secret = "test-secret-key-with-at-least-32-chars",
            AccessTokenMinutes = 15
        }));

        var user = new User
        {
            Id = 1,
            Name = "Admin User",
            Email = "admin@example.com",
            Role = UserRole.Admin
        };

        var (token, _) = service.GenerateAccessToken(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Contains(jwt.Claims, claim => claim.Type == ClaimTypes.Role && claim.Value == "Admin");
    }
}
