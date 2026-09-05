using AiVideoDetection.Domain.Constants;

namespace AiVideoDetection.Domain.Entities;

public class PaymentTransaction
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long SubscriptionPlanId { get; set; }
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public long? UserSubscriptionId { get; set; }
    public UserSubscription? UserSubscription { get; set; }
    public string Provider { get; set; } = PaymentProviders.Easypaisa;
    public string OrderId { get; set; } = string.Empty;
    public string? ProviderTransactionId { get; set; }
    public string Status { get; set; } = PaymentTransactionStatuses.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PKR";
    public string? FailureReason { get; set; }
    public string? GatewayRequestJson { get; set; }
    public string? GatewayResponseJson { get; set; }
    public string? CallbackPayloadJson { get; set; }
    public DateTimeOffset InitiatedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
