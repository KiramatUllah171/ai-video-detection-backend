using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Payments.DTOs;

namespace AiVideoDetection.Application.Payments.Interfaces;

public interface IPaymentService
{
    Task<ApiResponse<PaymentInitiationResponse>> InitiatePaymentAsync(
        long userId,
        InitiatePaymentRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PaymentStatusResponse>> GetPaymentStatusAsync(
        long userId,
        string orderId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PaymentStatusResponse>> CompleteMockPaymentAsync(
        long userId,
        string orderId,
        MockPaymentCompletionRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PaymentStatusResponse>> HandleProviderCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default);
}
