using AiVideoDetection.Application.Auth.DTOs;
using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Domain.Entities;
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
        var authService = CreateAuthService(dbContext);

        var first = await authService.SignupAsync(ValidSignupRequest("duplicate@example.com"), null);
        var second = await authService.SignupAsync(ValidSignupRequest("DUPLICATE@example.com"), null);

        Assert.True(first.Success);
        Assert.False(second.Success);
    }

    [Fact]
    public async Task ForgotPasswordAsync_ReturnsGenericSuccessForMissingEmail()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);

        var response = await authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "missing@example.com" });

        Assert.True(response.Success);
        Assert.Empty(emailSender.ResetUrls);
    }

    [Fact]
    public async Task ResetPasswordAsync_UpdatesPasswordAndRevokesToken()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new PasswordHasher();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender, passwordHasher);
        var signup = await authService.SignupAsync(ValidSignupRequest("reset@example.com"), null);
        Assert.True(signup.Success);

        var forgot = await authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "reset@example.com" });
        Assert.True(forgot.Success);
        var resetToken = new Uri(emailSender.ResetUrls.Single()).Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .First(parts => parts[0] == "token")[1];
        resetToken = Uri.UnescapeDataString(resetToken);

        var reset = await authService.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = resetToken,
            Password = "NewPassword123",
            ConfirmPassword = "NewPassword123"
        });

        Assert.True(reset.Success);
        var user = await dbContext.Users.SingleAsync(user => user.Email == "reset@example.com");
        Assert.True(passwordHasher.VerifyPassword("NewPassword123", user.PasswordHash));
        Assert.False(passwordHasher.VerifyPassword("Password123", user.PasswordHash));
        Assert.NotNull(await dbContext.PasswordResetTokens.SingleAsync(token => token.UserId == user.Id && token.UsedAt != null));
    }

    [Fact]
    public async Task SignupAsync_SendsEmailConfirmationLink()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);

        var signup = await authService.SignupAsync(ValidSignupRequest("confirm@example.com"), null);

        Assert.True(signup.Success);
        Assert.Single(emailSender.ConfirmationUrls);
        Assert.Single(dbContext.EmailConfirmationTokens);
    }

    [Fact]
    public async Task ConfirmEmailAsync_MarksUserEmailConfirmed()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("confirm-token@example.com"), null);
        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());

        var response = await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });

        Assert.True(response.Success);
        var user = await dbContext.Users.SingleAsync(user => user.Email == "confirm-token@example.com");
        Assert.True(user.EmailConfirmed);
        Assert.NotNull(await dbContext.EmailConfirmationTokens.SingleAsync(token => token.UserId == user.Id && token.UsedAt != null));
    }

    [Fact]
    public async Task ResendEmailConfirmationAsync_ReturnsGenericSuccessForMissingEmail()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);

        var response = await authService.ResendEmailConfirmationAsync(new ResendEmailConfirmationRequest { Email = "missing@example.com" });

        Assert.True(response.Success);
        Assert.Empty(emailSender.ConfirmationUrls);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    private static AuthService CreateAuthService(
        AppDbContext dbContext,
        TestPasswordResetEmailSender? emailSender = null,
        IPasswordHasher? passwordHasher = null)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            Secret = "test-secret-key-with-at-least-32-chars",
            AccessTokenMinutes = 15,
            RefreshTokenDays = 7
        });
        passwordHasher ??= new PasswordHasher();
        return new AuthService(
            dbContext,
            passwordHasher,
            new JwtTokenService(jwtOptions),
            jwtOptions,
            Options.Create(new PasswordResetOptions()),
            emailSender ?? new TestPasswordResetEmailSender(),
            emailSender ?? new TestPasswordResetEmailSender(),
            NullLogger<AuthService>.Instance);
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

    private sealed class TestPasswordResetEmailSender : IPasswordResetEmailSender
        , IEmailConfirmationSender
    {
        public List<string> ResetUrls { get; } = [];

        public List<string> ConfirmationUrls { get; } = [];

        public Task SendPasswordResetAsync(User user, string resetUrl, CancellationToken cancellationToken = default)
        {
            ResetUrls.Add(resetUrl);
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(User user, string confirmationUrl, CancellationToken cancellationToken = default)
        {
            ConfirmationUrls.Add(confirmationUrl);
            return Task.CompletedTask;
        }
    }

    private static string ExtractToken(string url)
    {
        return Uri.UnescapeDataString(
            new Uri(url).Query
                .TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(parts => parts.Length == 2)
                .First(parts => parts[0] == "token")[1]);
    }
}
