using AiVideoDetection.Application.Payments.DTOs;

namespace AiVideoDetection.Application.Payments.Interfaces;

public interface IPaymentGateway
{
    string Provider { get; }

    Task<PaymentGatewayInitiationResult> InitiatePaymentAsync(
        PaymentGatewayInitiationRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentGatewayVerificationResult> VerifyCallbackAsync(
        PaymentCallbackRequest request,
        CancellationToken cancellationToken = default);
}
