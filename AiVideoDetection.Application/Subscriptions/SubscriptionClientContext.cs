namespace AiVideoDetection.Application.Subscriptions;

public class SubscriptionClientContext
{
    public long? DeviceIdentityId { get; init; }
    public string? IpAddress { get; init; }
    public string? IpHash { get; init; }
}
