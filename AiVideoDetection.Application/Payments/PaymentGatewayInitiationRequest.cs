namespace AiVideoDetection.Application.Payments;

public class PaymentGatewayInitiationRequest
{
    public string OrderId { get; init; } = string.Empty;

    public long UserId { get; init; }

    public string PlanCode { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "PKR";

    public DateTimeOffset ExpiresAt { get; init; }
}
