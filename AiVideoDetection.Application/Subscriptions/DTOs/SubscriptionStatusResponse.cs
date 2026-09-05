namespace AiVideoDetection.Application.Subscriptions.DTOs;

public class SubscriptionStatusResponse
{
    public string PlanCode { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public bool IsAdmin { get; init; }
    public bool IsPaid { get; init; }
    public string SubscriptionStatus { get; init; } = string.Empty;
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public int? ScanLimit { get; init; }
    public int UsedScans { get; init; }
    public int ReservedScans { get; init; }
    public int? RemainingScans { get; init; }
    public long? MaxVideoSizeBytes { get; init; }
    public bool AllowsSmartScan { get; init; }
    public bool AllowsDetailedScan { get; init; }
    public FreeTrialStatusDto? FreeTrial { get; init; }
}
