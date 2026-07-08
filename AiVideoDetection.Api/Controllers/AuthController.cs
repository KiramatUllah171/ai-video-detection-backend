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
    IValidator<RefreshTokenRequest> refreshTokenValidator) : ControllerBase
{
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
        return ToActionResult(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AuthResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<RefreshTokenRequest, AuthResponse>(refreshTokenValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.RefreshAsync(request, GetIpAddress(), cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<bool>>> Logout(RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var validationResponse = await ValidateAsync<RefreshTokenRequest, bool>(refreshTokenValidator, request, cancellationToken);
        if (validationResponse is not null)
        {
            return BadRequest(validationResponse);
        }

        var response = await authService.LogoutAsync(request, cancellationToken);
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
