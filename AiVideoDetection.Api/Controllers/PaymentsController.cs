using System.Security.Claims;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Payments;
using AiVideoDetection.Application.Payments.DTOs;
using AiVideoDetection.Application.Payments.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController(IPaymentService paymentService) : ControllerBase
{
    [HttpPost("checkout")]
    [Authorize(Policy = "EmailConfirmed")]
    [ProducesResponseType(typeof(ApiResponse<PaymentInitiationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaymentInitiationResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PaymentInitiationResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PaymentInitiationResponse>>> Checkout(
        InitiatePaymentRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<PaymentInitiationResponse>.ErrorResponse("Unauthorized."));
        }

        var response = await paymentService.InitiatePaymentAsync(currentUserId, request, cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("{orderId}")]
    [Authorize(Policy = "EmailConfirmed")]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PaymentStatusResponse>>> Status(
        string orderId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<PaymentStatusResponse>.ErrorResponse("Unauthorized."));
        }

        var response = await paymentService.GetPaymentStatusAsync(currentUserId, orderId, cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("mock/{orderId}/complete")]
    [Authorize(Policy = "EmailConfirmed")]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PaymentStatusResponse>>> CompleteMock(
        string orderId,
        MockPaymentCompletionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<PaymentStatusResponse>.ErrorResponse("Unauthorized."));
        }

        var response = await paymentService.CompleteMockPaymentAsync(currentUserId, orderId, request, cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("easypaisa/callback")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PaymentStatusResponse>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiResponse<PaymentStatusResponse>>> EasypaisaCallback(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        var response = await paymentService.HandleProviderCallbackAsync(request, cancellationToken);
        return ToActionResult(response);
    }

    private bool TryGetCurrentUserId(out long currentUserId)
    {
        return long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out currentUserId);
    }

    private ActionResult<ApiResponse<T>> ToActionResult<T>(ApiResponse<T> response)
    {
        if (response.Success)
        {
            return Ok(response);
        }

        return response.ErrorCode switch
        {
            PaymentErrorCodes.PaymentNotFound => NotFound(response),
            PaymentErrorCodes.MockPaymentUnavailable => StatusCode(StatusCodes.Status403Forbidden, response),
            PaymentErrorCodes.PaymentGatewayUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            _ => BadRequest(response)
        };
    }
}
