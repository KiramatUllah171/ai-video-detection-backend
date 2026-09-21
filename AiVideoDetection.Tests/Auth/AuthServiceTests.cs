using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Admin.Interfaces;
using AiVideoDetection.Application.Common;
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

        var first = await authService.SignupAsync(ValidSignupRequest("duplicate@example.com"));
        var second = await authService.SignupAsync(ValidSignupRequest("DUPLICATE@example.com"));

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
    public async Task ForgotPasswordAsync_ThrottlesRepeatedRequestsForSameEmail()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(
            dbContext,
            authSecurityOptions: Options.Create(new AuthSecurityOptions
            {
                PasswordResetEmailPermitLimit = 1,
                EmailThrottleWindowMinutes = 15
            }));

        var first = await authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "missing@example.com" });
        var second = await authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "missing@example.com" });

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Contains("Too many requests", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResetPasswordAsync_UpdatesPasswordAndRevokesToken()
    {
        await using var dbContext = CreateDbContext();
        var passwordHasher = new PasswordHasher();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender, passwordHasher);
        var signup = await authService.SignupAsync(ValidSignupRequest("reset@example.com"));
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
            Password = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        });

        Assert.True(reset.Success);
        var user = await dbContext.Users.SingleAsync(user => user.Email == "reset@example.com");
        Assert.True(passwordHasher.VerifyPassword("NewPassword123!", user.PasswordHash));
        Assert.False(passwordHasher.VerifyPassword("Password123!", user.PasswordHash));
        var storedToken = await dbContext.PasswordResetTokens.SingleAsync(token => token.UserId == user.Id && token.UsedAt != null);
        Assert.NotEqual(resetToken, storedToken.TokenHash);
        Assert.Equal(64, storedToken.TokenHash.Length);
    }

    [Fact]
    public async Task ResetPasswordAsync_ReturnsMeaningfulMessageForUsedLink()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("used-reset@example.com"));
        await authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "used-reset@example.com" });
        var resetToken = ExtractToken(emailSender.ResetUrls.Single());

        var first = await authService.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = resetToken,
            Password = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        });
        var second = await authService.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = resetToken,
            Password = "AnotherPassword123!",
            ConfirmPassword = "AnotherPassword123!"
        });

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Contains("already been used", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckPasswordResetAsync_ReturnsReadyOnlyForUnusedLink()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("check-reset@example.com"));
        await authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "check-reset@example.com" });
        var resetToken = ExtractToken(emailSender.ResetUrls.Single());

        var beforeUse = await authService.CheckPasswordResetAsync(new ConfirmEmailRequest { Token = resetToken });
        await authService.ResetPasswordAsync(new ResetPasswordRequest
        {
            Token = resetToken,
            Password = "NewPassword123!",
            ConfirmPassword = "NewPassword123!"
        });
        var afterUse = await authService.CheckPasswordResetAsync(new ConfirmEmailRequest { Token = resetToken });

        Assert.True(beforeUse.Success);
        Assert.True(beforeUse.Data);
        Assert.True(afterUse.Success);
        Assert.False(afterUse.Data);
        Assert.Contains("already been used", afterUse.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SignupAsync_DoesNotCreateAuthenticatedSession()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(dbContext);

        var signup = await authService.SignupAsync(ValidSignupRequest("pending@example.com"));

        Assert.True(signup.Success);
        Assert.True(signup.Data);
        Assert.Empty(dbContext.RefreshTokens);
    }

    [Fact]
    public async Task SignupAsync_SendsEmailConfirmationLink()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);

        var signup = await authService.SignupAsync(ValidSignupRequest("confirm@example.com"));

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
        await authService.SignupAsync(ValidSignupRequest("confirm-token@example.com"));
        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());

        var response = await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });

        Assert.True(response.Success);
        var user = await dbContext.Users.SingleAsync(user => user.Email == "confirm-token@example.com");
        Assert.True(user.EmailConfirmed);
        var storedToken = await dbContext.EmailConfirmationTokens.SingleAsync(token => token.UserId == user.Id && token.UsedAt != null);
        Assert.NotEqual(confirmationToken, storedToken.TokenHash);
        Assert.Equal(64, storedToken.TokenHash.Length);
    }

    [Fact]
    public async Task CheckEmailConfirmationAsync_ReturnsReadyOnlyForUnusedToken()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("status@example.com"));
        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());

        var beforeUse = await authService.CheckEmailConfirmationAsync(new ConfirmEmailRequest { Token = confirmationToken });
        await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });
        var afterUse = await authService.CheckEmailConfirmationAsync(new ConfirmEmailRequest { Token = confirmationToken });

        Assert.True(beforeUse.Success);
        Assert.True(beforeUse.Data);
        Assert.True(afterUse.Success);
        Assert.False(afterUse.Data);
        Assert.Contains("already been used", afterUse.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmEmailAsync_ReturnsAlreadyUsedMessageForUsedLink()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("used-link@example.com"));
        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());

        var first = await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });
        var second = await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Contains("already been used", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_RequiresConfirmedEmail()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("login-confirm@example.com"));

        var beforeConfirmation = await authService.LoginAsync(
            new LoginRequest { Email = "login-confirm@example.com", Password = "Password123!" },
            null);

        Assert.False(beforeConfirmation.Success);
        Assert.Empty(dbContext.RefreshTokens);

        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());
        var confirmation = await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });
        Assert.True(confirmation.Success);

        var afterConfirmation = await authService.LoginAsync(
            new LoginRequest { Email = "login-confirm@example.com", Password = "Password123!" },
            null);

        Assert.True(afterConfirmation.Success);
        Assert.NotNull(afterConfirmation.Data);
        var refreshToken = await dbContext.RefreshTokens.SingleAsync();
        Assert.NotEqual(afterConfirmation.Data.RefreshToken, refreshToken.TokenHash);
        Assert.Equal(64, refreshToken.TokenHash.Length);
    }

    [Fact]
    public async Task LoginAsync_ReturnsSpecificMessagesForEmailAndPasswordFailures()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("specific-login@example.com"));
        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());
        await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });

        var missingEmail = await authService.LoginAsync(
            new LoginRequest { Email = "missing-login@example.com", Password = "Password123!" },
            null);
        var wrongPassword = await authService.LoginAsync(
            new LoginRequest { Email = "specific-login@example.com", Password = "WrongPassword123!" },
            null);

        Assert.False(missingEmail.Success);
        Assert.Contains("email address", missingEmail.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(wrongPassword.Success);
        Assert.Contains("password", wrongPassword.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_LocksAccountAfterRepeatedPasswordFailures()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(
            dbContext,
            emailSender,
            authSecurityOptions: Options.Create(new AuthSecurityOptions
            {
                MaxFailedAccessAttempts = 2,
                LockoutMinutes = 15
            }));
        await authService.SignupAsync(ValidSignupRequest("lockout@example.com"));
        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());
        await authService.ConfirmEmailAsync(new ConfirmEmailRequest { Token = confirmationToken });

        var firstFailure = await authService.LoginAsync(
            new LoginRequest { Email = "lockout@example.com", Password = "WrongPassword123!" },
            null);
        var secondFailure = await authService.LoginAsync(
            new LoginRequest { Email = "lockout@example.com", Password = "WrongPassword123!" },
            null);
        var correctPasswordAfterLockout = await authService.LoginAsync(
            new LoginRequest { Email = "lockout@example.com", Password = "Password123!" },
            null);

        var user = await dbContext.Users.SingleAsync(user => user.Email == "lockout@example.com");
        Assert.False(firstFailure.Success);
        Assert.False(secondFailure.Success);
        Assert.Contains("failed sign-in attempts", secondFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(correctPasswordAfterLockout.Success);
        Assert.NotNull(user.LockoutEnd);
        Assert.Equal(2, user.AccessFailedCount);
    }

    [Fact]
    public async Task LoginAsync_ThrottlesRepeatedRequestsForSameEmail()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(
            dbContext,
            authSecurityOptions: Options.Create(new AuthSecurityOptions
            {
                LoginEmailPermitLimit = 1,
                EmailThrottleWindowMinutes = 15
            }));

        var first = await authService.LoginAsync(
            new LoginRequest { Email = "missing-login@example.com", Password = "Password123!" },
            null);
        var second = await authService.LoginAsync(
            new LoginRequest { Email = "missing-login@example.com", Password = "Password123!" },
            null);

        Assert.False(first.Success);
        Assert.False(second.Success);
        Assert.Contains("Too many requests", second.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeclineEmailConfirmationAsync_RemovesUnconfirmedAccount()
    {
        await using var dbContext = CreateDbContext();
        var emailSender = new TestPasswordResetEmailSender();
        var authService = CreateAuthService(dbContext, emailSender);
        await authService.SignupAsync(ValidSignupRequest("not-me@example.com"));
        var confirmationToken = ExtractToken(emailSender.ConfirmationUrls.Single());

        var decline = await authService.DeclineEmailConfirmationAsync(new ConfirmEmailRequest { Token = confirmationToken });

        Assert.True(decline.Success);
        Assert.Empty(dbContext.Users);
        Assert.Empty(dbContext.EmailConfirmationTokens);

        var repeatDecline = await authService.DeclineEmailConfirmationAsync(new ConfirmEmailRequest { Token = confirmationToken });
        Assert.True(repeatDecline.Success);
        Assert.Contains("handled", repeatDecline.Message, StringComparison.OrdinalIgnoreCase);

        var status = await authService.CheckEmailConfirmationAsync(new ConfirmEmailRequest { Token = confirmationToken });
        Assert.True(status.Success);
        Assert.False(status.Data);

        var signupAgain = await authService.SignupAsync(ValidSignupRequest("not-me@example.com"));
        Assert.True(signupAgain.Success);
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

    [Fact]
    public async Task ResendEmailConfirmationAsync_ThrottlesRepeatedRequestsForSameEmail()
    {
        await using var dbContext = CreateDbContext();
        var authService = CreateAuthService(
            dbContext,
            authSecurityOptions: Options.Create(new AuthSecurityOptions
            {
                EmailConfirmationResendPermitLimit = 1,
                EmailThrottleWindowMinutes = 15
            }));

        var first = await authService.ResendEmailConfirmationAsync(new ResendEmailConfirmationRequest { Email = "missing@example.com" });
        var second = await authService.ResendEmailConfirmationAsync(new ResendEmailConfirmationRequest { Email = "missing@example.com" });

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Contains("Too many requests", second.Message, StringComparison.OrdinalIgnoreCase);
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
        IPasswordHasher? passwordHasher = null,
        IOptions<AuthSecurityOptions>? authSecurityOptions = null)
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
            Options.Create(new GoogleAuthOptions()),
            Options.Create(new FacebookAuthOptions()),
            Options.Create(new PasswordResetOptions()),
            authSecurityOptions ?? Options.Create(new AuthSecurityOptions()),
            new InMemoryAuthThrottleService(),
            emailSender ?? new TestPasswordResetEmailSender(),
            emailSender ?? new TestPasswordResetEmailSender(),
            new NoopAuditLogService(),
            new TestHttpClientFactory(),
            NullLogger<AuthService>.Instance);
    }

    private static SignupRequest ValidSignupRequest(string email)
    {
        return new SignupRequest
        {
            Name = "Test User",
            Email = email,
            Password = "Password123!",
            ConfirmPassword = "Password123!"
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

    private sealed class NoopAuditLogService : IAuditLogService
    {
        public Task LogAsync(AuditLogCreateDto entry, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<ApiResponse<PagedResponse<AdminAuditLogDto>>> GetLogsAsync(
            int page,
            int pageSize,
            string? search,
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? severity,
            string? category,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ApiResponse<PagedResponse<AdminAuditLogDto>>.SuccessResponse(new PagedResponse<AdminAuditLogDto>()));
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
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
