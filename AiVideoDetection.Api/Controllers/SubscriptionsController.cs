using System.Security.Claims;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.DTOs;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmailConfirmed")]
[Route("api/subscriptions")]
public class SubscriptionsController(
    IEntitlementService entitlementService,
    IDeviceIdentityService deviceIdentityService) : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionStatusResponse>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<SubscriptionStatusResponse>), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiResponse<SubscriptionStatusResponse>>> Status(CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId))
        {
            return Unauthorized(ApiResponse<SubscriptionStatusResponse>.ErrorResponse("Unauthorized."));
        }

        SubscriptionClientContext? clientContext = null;
        try
        {
            clientContext = await deviceIdentityService.ResolveAsync(currentUserId, cancellationToken);
        }
        catch (InvalidOperationException exception)
            when (exception.Message == SubscriptionErrorCodes.SubscriptionSecurityNotConfigured)
        {
            return StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse<SubscriptionStatusResponse>.ErrorResponse(
                    "Subscription security is not configured.",
                    errorCode: SubscriptionErrorCodes.SubscriptionSecurityNotConfigured));
        }

        var response = await entitlementService.GetStatusAsync(currentUserId, clientContext, cancellationToken);
        return response.Success ? Ok(response) : BadRequest(response);
    }
}
