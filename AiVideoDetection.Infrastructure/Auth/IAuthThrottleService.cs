namespace AiVideoDetection.Infrastructure.Auth;

public interface IAuthThrottleService
{
    ThrottleCheckResult Check(string scope, string key, int permitLimit, TimeSpan window);
}

public sealed record ThrottleCheckResult(bool IsAllowed, TimeSpan? RetryAfter);
