using AiVideoDetection.Domain.Constants;

namespace AiVideoDetection.Domain.Entities;

public class ScanReservation
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long? UserSubscriptionId { get; set; }
    public UserSubscription? UserSubscription { get; set; }
    public long SubscriptionPlanId { get; set; }
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public long? DeviceIdentityId { get; set; }
    public DeviceIdentity? DeviceIdentity { get; set; }
    public long? VideoId { get; set; }
    public Video? Video { get; set; }
    public long? AnalysisJobId { get; set; }
    public AnalysisJob? AnalysisJob { get; set; }
    public string ReservationKind { get; set; } = ScanReservationKinds.FreeTrial;
    public string Status { get; set; } = ScanReservationStatuses.Reserved;
    public string AnalysisMode { get; set; } = "Basic";
    public long FileSizeBytes { get; set; }
    public string? FreeTrialIpHash { get; set; }
    public DateTimeOffset ReservedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
    public string? ReleaseReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
