using System.Security.Claims;
using AiVideoDetection.Application.Auth.DTOs;
using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Application.Common;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    IAuthService authService,
    IValidator<SignupRequest> signupValidator,
    IValidator<LoginRequest> loginValidator,
    IValidator<RefreshTokenRequest> refreshTokenValidator,
    IValidator<ForgotPasswordRequest> forgotPasswordValidator,
    IValidator<ResetPasswordRequest> resetPasswordValidator,
    IValidator<ConfirmEmailRequest> confirmEmailValidator,
    IValidator<ResendEmailConfirmationRequest> resendEmailConfirmationValidator,
    IWebHostEnvironment environment) : ControllerBase
{
    private const string RefreshTokenCookieName = "ai_video_refresh";

    [HttpPost("signup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Signup(SignupRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<SignupRequest, AuthResponse>(signupValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.SignupAsync(request, GetIpAddress(), cancellationToken);
        AttachRefreshTokenCookie(response);
        return ToActionResult(response);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<LoginRequest, AuthResponse>(loginValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.LoginAsync(request, GetIpAddress(), cancellationToken);
        AttachRefreshTokenCookie(response);
        return ToActionResult(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        request = WithRefreshTokenFromCookie(request);
        var validationResponse = await ValidateAsync<RefreshTokenRequest, AuthResponse>(refreshTokenValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.RefreshAsync(request, GetIpAddress(), cancellationToken);
        AttachRefreshTokenCookie(response);
        return ToActionResult(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        request = WithRefreshTokenFromCookie(request);
        var validationResponse = await ValidateAsync<RefreshTokenRequest, bool>(refreshTokenValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.LogoutAsync(request, cancellationToken);
        ClearRefreshTokenCookie();
        return ToActionResult(response);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<ForgotPasswordRequest, bool>(forgotPasswordValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.ForgotPasswordAsync(request, cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<ResetPasswordRequest, bool>(resetPasswordValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.ResetPasswordAsync(request, cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<ConfirmEmailRequest, bool>(confirmEmailValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.ConfirmEmailAsync(request, cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("resend-confirmation-email")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> ResendEmailConfirmation(ResendEmailConfirmationRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<ResendEmailConfirmationRequest, bool>(resendEmailConfirmationValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.ResendEmailConfirmationAsync(request, cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CurrentUserResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<CurrentUserResponse>>> Me(CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized(ApiResponse<CurrentUserResponse>.ErrorResponse("Unauthorized."));
        }

        var response = await authService.GetCurrentUserAsync(userId, cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("admin-check")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status403Forbidden)]
    public ActionResult<ApiResponse<string>> AdminCheck()
    {
        return Ok(ApiResponse<string>.SuccessResponse("Admin authorization is working."));
    }

    private string? GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private RefreshTokenRequest WithRefreshTokenFromCookie(RefreshTokenRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return request;
        }

        return Request.Cookies.TryGetValue(RefreshTokenCookieName, out var refreshToken)
            ? new RefreshTokenRequest { RefreshToken = refreshToken }
            : request;
    }

    private void AttachRefreshTokenCookie(ApiResponse<AuthResponse> response)
    {
        if (!response.Success || response.Data is null || string.IsNullOrWhiteSpace(response.Data.RefreshToken))
        {
            return;
        }

        Response.Cookies.Append(
            RefreshTokenCookieName,
            response.Data.RefreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddDays(7)
            });
        response.Data.RefreshToken = string.Empty;
    }

    private void ClearRefreshTokenCookie()
    {
        Response.Cookies.Delete(
            RefreshTokenCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = !environment.IsDevelopment(),
                SameSite = SameSiteMode.Lax
            });
    }

    private static async Task<ApiResponse<TResponse>?> ValidateAsync<TRequest, TResponse>(
        IValidator<TRequest> validator,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (validationResult.IsValid)
        {
            return null;
        }

        return ApiResponse<TResponse>.ErrorResponse(
            "Validation failed.",
            validationResult.Errors.Select(error => error.ErrorMessage));
    }

    private ActionResult<ApiResponse<T>> ToActionResult<T>(ApiResponse<T> response)
    {
        return response.Success ? Ok(response) : BadRequest(response);
    }
}
