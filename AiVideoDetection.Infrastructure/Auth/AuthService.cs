using System.Net;
using System.Security.Cryptography;
using System.Text;
using AiVideoDetection.Application.Auth.DTOs;
using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Auth;

public class AuthService(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IOptions<JwtOptions> jwtOptions,
    IOptions<PasswordResetOptions> passwordResetOptions,
    IPasswordResetEmailSender passwordResetEmailSender,
    IEmailConfirmationSender emailConfirmationSender,
    ILogger<AuthService> logger) : IAuthService
{
    private const string EmailNotFoundMessage = "We could not find an account with this email address.";
    private const string InvalidPasswordMessage = "The password you entered is incorrect. Please try again.";
    private const string ForgotPasswordMessage = "If an eligible account exists for this email address, a secure password reset link has been sent.";
    private const string InvalidResetTokenMessage = "This password reset link is invalid or has expired. Please request a new link.";
    private const string UsedResetTokenMessage = "This password reset link has already been used. Please request a new reset link if you need to change your password again.";
    private const string ResendConfirmationMessage = "If this account exists and still needs verification, a new email confirmation link has been sent.";
    private const string InvalidConfirmationTokenMessage = "This email confirmation link is invalid, expired, or has already been handled. Please request a new verification link if needed.";
    private const string ConfirmationTokenAlreadyUsedMessage = "This email confirmation link has already been used.";
    private const string EmailNotConfirmedMessage = "Please confirm your email address before signing in.";
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;
    private readonly PasswordResetOptions _passwordResetOptions = passwordResetOptions.Value;

    public async Task<ApiResponse<bool>> SignupAsync(
        SignupRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var emailExists = await dbContext.Users
            .AnyAsync(user => user.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            logger.LogInformation("Signup rejected because email already exists.");
            return ApiResponse<bool>.ErrorResponse("This email address is already registered. Please sign in or use another email address.");
        }

        var user = new User
        {
            Name = request.Name.Trim(),
            Email = normalizedEmail,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            Role = UserRole.User,
            IsActive = true,
            EmailConfirmed = false
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("User signup succeeded for user id {UserId}.", user.Id);
        await CreateAndSendEmailConfirmationAsync(user, cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Account created successfully. Please confirm your email address before signing in.");
    }

    public async Task<ApiResponse<AuthResponse>> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(existingUser => existingUser.Email == normalizedEmail, cancellationToken);

        if (user is null)
        {
            logger.LogInformation("Login failed because the email address was not found.");
            return ApiResponse<AuthResponse>.ErrorResponse(EmailNotFoundMessage);
        }

        if (!passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            logger.LogInformation("Login failed because the password was incorrect for user id {UserId}.", user.Id);
            return ApiResponse<AuthResponse>.ErrorResponse(InvalidPasswordMessage);
        }

        if (!user.IsActive)
        {
            logger.LogInformation("Inactive user login rejected for user id {UserId}.", user.Id);
            return ApiResponse<AuthResponse>.ErrorResponse("User account is inactive.");
        }

        if (!user.EmailConfirmed)
        {
            logger.LogInformation("Login rejected because email is not confirmed for user id {UserId}.", user.Id);
            return ApiResponse<AuthResponse>.ErrorResponse(EmailNotConfirmedMessage);
        }

        logger.LogInformation("Login succeeded for user id {UserId}.", user.Id);

        var response = await CreateAuthResponseAsync(user, ipAddress, cancellationToken);
        return ApiResponse<AuthResponse>.SuccessResponse(response, "Signed in successfully.");
    }

    public async Task<ApiResponse<AuthResponse>> RefreshAsync(
        RefreshTokenRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashRefreshToken(request.RefreshToken);
        var refreshToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (refreshToken is null || refreshToken.RevokedAt is not null || refreshToken.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            logger.LogInformation("Refresh token request rejected.");
            return ApiResponse<AuthResponse>.ErrorResponse("Invalid refresh token.");
        }

        if (!refreshToken.User.IsActive)
        {
            logger.LogInformation("Refresh token request rejected for inactive user id {UserId}.", refreshToken.UserId);
            return ApiResponse<AuthResponse>.ErrorResponse("User account is inactive.");
        }

        if (!refreshToken.User.EmailConfirmed)
        {
            logger.LogInformation("Refresh token request rejected because email is not confirmed for user id {UserId}.", refreshToken.UserId);
            return ApiResponse<AuthResponse>.ErrorResponse(EmailNotConfirmedMessage);
        }

        refreshToken.RevokedAt = DateTimeOffset.UtcNow;
        var response = await CreateAuthResponseAsync(refreshToken.User, ipAddress, cancellationToken);

        logger.LogInformation("Refresh token rotated for user id {UserId}.", refreshToken.UserId);

        return ApiResponse<AuthResponse>.SuccessResponse(response, "Token refreshed successfully.");
    }

    public async Task<ApiResponse<bool>> LogoutAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashRefreshToken(request.RefreshToken);
        var refreshToken = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (refreshToken is not null && refreshToken.RevokedAt is null)
        {
            refreshToken.RevokedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation("Refresh token revoked for user id {UserId}.", refreshToken.UserId);
        }

        return ApiResponse<bool>.SuccessResponse(true, "Logout successful.");
    }

    public async Task<ApiResponse<bool>> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(existingUser => existingUser.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive)
        {
            logger.LogInformation("Password reset requested for a non-existent or inactive account.");
            return ApiResponse<bool>.SuccessResponse(true, ForgotPasswordMessage);
        }

        var resetToken = GenerateRefreshToken();
        var resetTokenHash = HashToken(resetToken);
        var now = DateTimeOffset.UtcNow;

        var outstandingTokens = await dbContext.PasswordResetTokens
            .Where(token => token.UserId == user.Id && token.UsedAt == null && token.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var token in outstandingTokens)
        {
            token.UsedAt = now;
        }

        dbContext.PasswordResetTokens.Add(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = resetTokenHash,
            ExpiresAt = now.AddMinutes(Math.Clamp(_passwordResetOptions.TokenLifetimeMinutes, 5, 240))
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        var resetUrl = BuildPasswordResetUrl(resetToken);
        await passwordResetEmailSender.SendPasswordResetAsync(user, resetUrl, cancellationToken);

        logger.LogInformation("Password reset email delivery requested for user id {UserId}.", user.Id);
        return ApiResponse<bool>.SuccessResponse(true, ForgotPasswordMessage);
    }

    public async Task<ApiResponse<bool>> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;
        var resetToken = await dbContext.PasswordResetTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (resetToken is null || resetToken.ExpiresAt <= now)
        {
            logger.LogInformation("Password reset rejected because the token is invalid or expired.");
            return ApiResponse<bool>.ErrorResponse(InvalidResetTokenMessage);
        }

        if (resetToken.UsedAt is not null)
        {
            logger.LogInformation("Password reset rejected because the token was already used for user id {UserId}.", resetToken.UserId);
            return ApiResponse<bool>.ErrorResponse(UsedResetTokenMessage);
        }

        if (!resetToken.User.IsActive)
        {
            logger.LogInformation("Password reset rejected for inactive user id {UserId}.", resetToken.UserId);
            return ApiResponse<bool>.ErrorResponse(InvalidResetTokenMessage);
        }

        resetToken.User.PasswordHash = passwordHasher.HashPassword(request.Password);
        resetToken.UsedAt = now;

        var activeRefreshTokens = await dbContext.RefreshTokens
            .Where(token => token.UserId == resetToken.UserId && token.RevokedAt == null && token.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in activeRefreshTokens)
        {
            refreshToken.RevokedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Password reset succeeded for user id {UserId}.", resetToken.UserId);
        return ApiResponse<bool>.SuccessResponse(true, "Your password has been reset successfully.");
    }

    public async Task<ApiResponse<bool>> CheckPasswordResetAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;
        var resetToken = await dbContext.PasswordResetTokens
            .Include(token => token.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (resetToken is null || resetToken.ExpiresAt <= now || !resetToken.User.IsActive)
        {
            logger.LogInformation("Password reset status checked for invalid or expired token.");
            return ApiResponse<bool>.SuccessResponse(false, InvalidResetTokenMessage);
        }

        if (resetToken.UsedAt is not null)
        {
            logger.LogInformation("Password reset status checked for already used token for user id {UserId}.", resetToken.UserId);
            return ApiResponse<bool>.SuccessResponse(false, UsedResetTokenMessage);
        }

        return ApiResponse<bool>.SuccessResponse(true, "This password reset link is ready to use.");
    }

    public async Task<ApiResponse<bool>> ConfirmEmailAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;
        var confirmationToken = await dbContext.EmailConfirmationTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (confirmationToken is null || confirmationToken.ExpiresAt <= now)
        {
            logger.LogInformation("Email confirmation rejected because the token is invalid or expired.");
            return ApiResponse<bool>.ErrorResponse(InvalidConfirmationTokenMessage);
        }

        if (confirmationToken.UsedAt is not null)
        {
            logger.LogInformation("Email confirmation skipped because the token was already used for user id {UserId}.", confirmationToken.UserId);
            return ApiResponse<bool>.SuccessResponse(true, ConfirmationTokenAlreadyUsedMessage);
        }

        if (!confirmationToken.User.IsActive)
        {
            logger.LogInformation("Email confirmation rejected for inactive user id {UserId}.", confirmationToken.UserId);
            return ApiResponse<bool>.ErrorResponse(InvalidConfirmationTokenMessage);
        }

        confirmationToken.User.EmailConfirmed = true;
        confirmationToken.UsedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Email confirmed for user id {UserId}.", confirmationToken.UserId);
        return ApiResponse<bool>.SuccessResponse(true, "Your email address has been confirmed successfully.");
    }

    public async Task<ApiResponse<bool>> CheckEmailConfirmationAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;
        var confirmationToken = await dbContext.EmailConfirmationTokens
            .Include(token => token.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (confirmationToken is null || confirmationToken.ExpiresAt <= now || !confirmationToken.User.IsActive)
        {
            logger.LogInformation("Email confirmation status checked for invalid, expired, or handled token.");
            return ApiResponse<bool>.SuccessResponse(false, "This verification request is invalid, expired, or has already been handled.");
        }

        if (confirmationToken.UsedAt is not null)
        {
            logger.LogInformation("Email confirmation status checked for already used token for user id {UserId}.", confirmationToken.UserId);
            return ApiResponse<bool>.SuccessResponse(false, ConfirmationTokenAlreadyUsedMessage);
        }

        if (confirmationToken.User.EmailConfirmed)
        {
            logger.LogInformation("Email confirmation status checked for already confirmed user id {UserId}.", confirmationToken.UserId);
            return ApiResponse<bool>.SuccessResponse(false, "This email address is already confirmed.");
        }

        return ApiResponse<bool>.SuccessResponse(true, "This verification request is ready to confirm.");
    }

    public async Task<ApiResponse<bool>> DeclineEmailConfirmationAsync(
        ConfirmEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(request.Token);
        var now = DateTimeOffset.UtcNow;
        var confirmationToken = await dbContext.EmailConfirmationTokens
            .Include(token => token.User)
            .FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

        if (confirmationToken is null || confirmationToken.ExpiresAt <= now)
        {
            logger.LogInformation("Email confirmation decline ignored because the token is invalid or expired.");
            return ApiResponse<bool>.SuccessResponse(true, "This verification request is invalid, expired, or has already been handled.");
        }

        if (confirmationToken.UsedAt is not null)
        {
            logger.LogInformation("Email confirmation decline ignored because the token was already used for user id {UserId}.", confirmationToken.UserId);
            return ApiResponse<bool>.SuccessResponse(true, "This verification request has already been handled.");
        }

        if (confirmationToken.User.EmailConfirmed)
        {
            logger.LogInformation("Email confirmation decline ignored because user id {UserId} is already confirmed.", confirmationToken.UserId);
            return ApiResponse<bool>.SuccessResponse(true, "This email address is already confirmed.");
        }

        confirmationToken.UsedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Deleting unconfirmed user id {UserId} after email holder declined confirmation.", confirmationToken.UserId);
        dbContext.Users.Remove(confirmationToken.User);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "This verification request was declined. The unconfirmed account has been removed.");
    }

    public async Task<ApiResponse<bool>> ResendEmailConfirmationAsync(
        ResendEmailConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(existingUser => existingUser.Email == normalizedEmail, cancellationToken);

        if (user is null || !user.IsActive || user.EmailConfirmed)
        {
            logger.LogInformation("Email confirmation resend requested for a non-existent, inactive, or already confirmed account.");
            return ApiResponse<bool>.SuccessResponse(true, ResendConfirmationMessage);
        }

        await CreateAndSendEmailConfirmationAsync(user, cancellationToken);
        logger.LogInformation("Email confirmation resent for user id {UserId}.", user.Id);
        return ApiResponse<bool>.SuccessResponse(true, ResendConfirmationMessage);
    }

    public async Task<ApiResponse<CurrentUserResponse>> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(existingUser => existingUser.Id == userId, cancellationToken);

        if (user is null)
        {
            return ApiResponse<CurrentUserResponse>.ErrorResponse("User was not found.");
        }

        var response = new CurrentUserResponse
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            EmailConfirmed = user.EmailConfirmed
        };

        return ApiResponse<CurrentUserResponse>.SuccessResponse(response);
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(User user, string? ipAddress, CancellationToken cancellationToken)
    {
        var (accessToken, expiresAt) = jwtTokenService.GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();
        var refreshTokenHash = HashRefreshToken(refreshToken);

        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = DateTimeOffset.UtcNow.Add(_jwtOptions.GetRefreshTokenLifetime()),
            CreatedByIp = ParseIpAddress(ipAddress)
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = expiresAt,
            User = MapUser(user)
        };
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    }

    private static string HashRefreshToken(string refreshToken)
    {
        return HashToken(refreshToken);
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private string BuildPasswordResetUrl(string resetToken)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_passwordResetOptions.FrontendBaseUrl)
            ? "http://localhost:5173"
            : _passwordResetOptions.FrontendBaseUrl.TrimEnd('/');
        return $"{baseUrl}/reset-password?token={Uri.EscapeDataString(resetToken)}";
    }

    private async Task CreateAndSendEmailConfirmationAsync(User user, CancellationToken cancellationToken)
    {
        var confirmationToken = GenerateRefreshToken();
        var confirmationTokenHash = HashToken(confirmationToken);
        var now = DateTimeOffset.UtcNow;

        var outstandingTokens = await dbContext.EmailConfirmationTokens
            .Where(token => token.UserId == user.Id && token.UsedAt == null && token.ExpiresAt > now)
            .ToListAsync(cancellationToken);
        foreach (var token in outstandingTokens)
        {
            token.UsedAt = now;
        }

        dbContext.EmailConfirmationTokens.Add(new EmailConfirmationToken
        {
            UserId = user.Id,
            TokenHash = confirmationTokenHash,
            ExpiresAt = now.AddHours(Math.Clamp(_passwordResetOptions.EmailConfirmationTokenLifetimeHours, 1, 168))
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await emailConfirmationSender.SendEmailConfirmationAsync(user, BuildEmailConfirmationUrl(confirmationToken), cancellationToken);
    }

    private string BuildEmailConfirmationUrl(string confirmationToken)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_passwordResetOptions.FrontendBaseUrl)
            ? "http://localhost:5173"
            : _passwordResetOptions.FrontendBaseUrl.TrimEnd('/');
        return $"{baseUrl}/confirm-email?token={Uri.EscapeDataString(confirmationToken)}";
    }

    private static IPAddress? ParseIpAddress(string? ipAddress)
    {
        return IPAddress.TryParse(ipAddress, out var parsedIpAddress)
            ? parsedIpAddress
            : null;
    }

    private static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    private static UserDto MapUser(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed
        };
    }
}
