using AiVideoDetection.Application.Auth.DTOs;
using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Infrastructure.Auth;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Auth;

public class AuthServiceTests
{
    [Fact]
    public async Task SignupAsync_DoesNotAllowDuplicateEmail()
    {
        await using var dbContext = CreateDbContext();
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Secret = "test-secret-key-with-at-least-32-chars",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });
        IPasswordHasher passwordHasher = new PasswordHasher();
        var authService = new AuthService(
            dbContext,
            passwordHasher,
            new JwtTokenService(jwtOptions),
            jwtOptions,
            NullLogger<AuthService>.Instance);

        var first = await authService.SignupAsync(ValidSignupRequest("duplicate@example.com"), null);
        var second = await authService.SignupAsync(ValidSignupRequest("DUPLICATE@example.com"), null);

        Assert.True(first.Success);
        Assert.False(second.Success);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static SignupRequest ValidSignupRequest(string email)
    {
        return new SignupRequest
        {
            Name = "Test User",
            Email = email,
            Password = "Password123",
            ConfirmPassword = "Password123"
        };
    }
}
