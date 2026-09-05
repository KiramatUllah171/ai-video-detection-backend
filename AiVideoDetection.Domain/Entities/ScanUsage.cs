using AiVideoDetection.Domain.Constants;

namespace AiVideoDetection.Domain.Entities;

public class ScanUsage
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long? UserSubscriptionId { get; set; }
    public UserSubscription? UserSubscription { get; set; }
    public long SubscriptionPlanId { get; set; }
    public SubscriptionPlan SubscriptionPlan { get; set; } = null!;
    public long? ScanReservationId { get; set; }
    public ScanReservation? ScanReservation { get; set; }
    public long? VideoId { get; set; }
    public Video? Video { get; set; }
    public long? AnalysisJobId { get; set; }
    public AnalysisJob? AnalysisJob { get; set; }
    public string EntitlementType { get; set; } = ScanReservationKinds.FreeTrial;
    public string AnalysisMode { get; set; } = "Basic";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
