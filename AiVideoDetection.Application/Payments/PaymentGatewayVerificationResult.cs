namespace AiVideoDetection.Application.Payments;

public class PaymentGatewayVerificationResult
{
    public bool Success { get; init; }

    public string OrderId { get; init; } = string.Empty;

    public string? ProviderTransactionId { get; init; }

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "PKR";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static PaymentGatewayVerificationResult Succeeded(
        string orderId,
        string? providerTransactionId,
        decimal amount,
        string currency)
    {
        return new PaymentGatewayVerificationResult
        {
            Success = true,
            OrderId = orderId,
            ProviderTransactionId = providerTransactionId,
            Amount = amount,
            Currency = currency
        };
    }

    public static PaymentGatewayVerificationResult Failed(
        string orderId,
        string errorCode,
        string errorMessage)
    {
        return new PaymentGatewayVerificationResult
        {
            Success = false,
            OrderId = orderId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
    }
}
