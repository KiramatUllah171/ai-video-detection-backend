namespace AiVideoDetection.Application.Payments;

public class PaymentGatewayInitiationResult
{
    public bool Success { get; init; }

    public string? PaymentUrl { get; init; }

    public string? ProviderTransactionId { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static PaymentGatewayInitiationResult Succeeded(string? paymentUrl, string? providerTransactionId = null)
    {
        return new PaymentGatewayInitiationResult
        {
            Success = true,
            PaymentUrl = paymentUrl,
            ProviderTransactionId = providerTransactionId
        };
    }

    public static PaymentGatewayInitiationResult Failed(string errorCode, string errorMessage)
    {
        return new PaymentGatewayInitiationResult
        {
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
