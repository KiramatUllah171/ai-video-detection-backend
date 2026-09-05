namespace AiVideoDetection.Application.Payments.DTOs;

public class PaymentInitiationResponse
{
    public string OrderId { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "PKR";

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? PaymentUrl { get; init; }

    public bool IsMock { get; init; }
}
