namespace AiVideoDetection.Application.Payments.DTOs;

public class PaymentStatusResponse
{
    public string OrderId { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string PlanCode { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    public decimal Amount { get; init; }

    public string Currency { get; init; } = "PKR";

    public string? ProviderTransactionId { get; init; }

    public DateTimeOffset InitiatedAt { get; init; }

    public DateTimeOffset? VerifiedAt { get; init; }

    public DateTimeOffset? FailedAt { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public long? SubscriptionId { get; init; }

    public DateTimeOffset? SubscriptionStartsAt { get; init; }

    public DateTimeOffset? SubscriptionExpiresAt { get; init; }

    public string? FailureReason { get; init; }
}
