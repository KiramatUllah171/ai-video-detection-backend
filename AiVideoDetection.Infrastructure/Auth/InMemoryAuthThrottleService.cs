using System.Collections.Concurrent;

namespace AiVideoDetection.Infrastructure.Auth;

public sealed class InMemoryAuthThrottleService : IAuthThrottleService
{
    private readonly ConcurrentDictionary<string, ThrottleCounter> _counters = new(StringComparer.Ordinal);

    public ThrottleCheckResult Check(string scope, string key, int permitLimit, TimeSpan window)
    {
        if (permitLimit <= 0 || window <= TimeSpan.Zero)
        {
            return new ThrottleCheckResult(IsAllowed: true, RetryAfter: null);
        }

        var now = DateTimeOffset.UtcNow;
        var counterKey = $"{scope}:{key.Trim().ToLowerInvariant()}";
        var counter = _counters.AddOrUpdate(
            counterKey,
            _ => new ThrottleCounter(Count: 1, WindowExpiresAt: now.Add(window)),
            (_, existing) =>
            {
                if (existing.WindowExpiresAt <= now)
                {
                    return new ThrottleCounter(Count: 1, WindowExpiresAt: now.Add(window));
                }

                return existing with { Count = existing.Count + 1 };
            });

        CleanupExpiredCounters(now);

        return counter.Count <= permitLimit
            ? new ThrottleCheckResult(IsAllowed: true, RetryAfter: null)
            : new ThrottleCheckResult(IsAllowed: false, RetryAfter: counter.WindowExpiresAt - now);
    }

    private void CleanupExpiredCounters(DateTimeOffset now)
    {
        foreach (var entry in _counters)
        {
            if (entry.Value.WindowExpiresAt <= now)
            {
                _counters.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed record ThrottleCounter(int Count, DateTimeOffset WindowExpiresAt);
}
