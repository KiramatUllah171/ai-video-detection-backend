namespace AiVideoDetection.Application.Subscriptions.Interfaces;

public interface IDeviceIdentityService
{
    Task<SubscriptionClientContext> ResolveAsync(long userId, CancellationToken cancellationToken = default);

    Task<SubscriptionClientContext> ResolveAnonymousAsync(CancellationToken cancellationToken = default);
}
