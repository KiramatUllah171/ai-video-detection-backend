using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Application.Subscriptions;

public class ScanReservationRequest
{
    public long UserId { get; init; }
    public AnalysisMode AnalysisMode { get; init; }
    public long FileSizeBytes { get; init; }
    public SubscriptionClientContext? ClientContext { get; init; }
}
