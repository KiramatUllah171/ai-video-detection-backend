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
    ILogger<AuthService> logger) : IAuthService
{
    private const string InvalidLoginMessage = "Invalid email or password.";
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<ApiResponse<AuthResponse>> SignupAsync(
        SignupRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var emailExists = await dbContext.Users
            .AnyAsync(user => user.Email == normalizedEmail, cancellationToken);

        if (emailExists)
        {
            logger.LogInformation("Signup rejected because email already exists.");
            return ApiResponse<AuthResponse>.ErrorResponse("Email is already registered.");
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

        var response = await CreateAuthResponseAsync(user, ipAddress, cancellationToken);
        return ApiResponse<AuthResponse>.SuccessResponse(response, "Signup successful.");
    }

    public async Task<ApiResponse<AuthResponse>> LoginAsync(
        LoginRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await dbContext.Users
            .FirstOrDefaultAsync(existingUser => existingUser.Email == normalizedEmail, cancellationToken);

        if (user is null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            logger.LogInformation("Login failed.");
            return ApiResponse<AuthResponse>.ErrorResponse(InvalidLoginMessage);
        }

        if (!user.IsActive)
        {
            logger.LogInformation("Inactive user login rejected for user id {UserId}.", user.Id);
            return ApiResponse<AuthResponse>.ErrorResponse("User account is inactive.");
        }

        logger.LogInformation("Login succeeded for user id {UserId}.", user.Id);

        var response = await CreateAuthResponseAsync(user, ipAddress, cancellationToken);
        return ApiResponse<AuthResponse>.SuccessResponse(response, "Login successful.");
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
            Role = user.Role
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
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_jwtOptions.RefreshTokenDays),
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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes);
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
