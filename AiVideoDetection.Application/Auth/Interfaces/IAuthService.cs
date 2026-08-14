using AiVideoDetection.Application.Auth.DTOs;
using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Application.Auth.Interfaces;

public interface IAuthService
{
    Task<ApiResponse<bool>> SignupAsync(SignupRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<AuthResponse>> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<ApiResponse<AuthResponse>> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> LogoutAsync(RefreshTokenRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> CheckPasswordResetAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> CheckEmailConfirmationAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeclineEmailConfirmationAsync(ConfirmEmailRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> ResendEmailConfirmationAsync(ResendEmailConfirmationRequest request, CancellationToken cancellationToken = default);

    Task<ApiResponse<CurrentUserResponse>> GetCurrentUserAsync(long userId, CancellationToken cancellationToken = default);
}
