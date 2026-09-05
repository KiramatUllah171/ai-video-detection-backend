namespace AiVideoDetection.Application.Subscriptions.Interfaces;

public interface IDeviceIdentityService
{
    Task<SubscriptionClientContext> ResolveAsync(long userId, CancellationToken cancellationToken = default);
}
