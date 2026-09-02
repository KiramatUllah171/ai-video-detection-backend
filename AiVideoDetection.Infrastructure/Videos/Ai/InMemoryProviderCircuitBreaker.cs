using System.Collections.Concurrent;

namespace AiVideoDetection.Infrastructure.Videos.Ai;

public sealed class InMemoryProviderCircuitBreaker : IProviderCircuitBreaker
{
    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);

    public ProviderCircuitSnapshot GetSnapshot(string providerKey)
    {
        var state = _states.GetOrAdd(Normalize(providerKey), _ => new CircuitState());
        lock (state.SyncRoot)
        {
            return new ProviderCircuitSnapshot(
                state.OpenUntil is not null && state.OpenUntil > DateTimeOffset.UtcNow,
                state.ConsecutiveFailures,
                state.OpenUntil);
        }
    }

    public bool CanExecute(string providerKey, DateTimeOffset now)
    {
        var state = _states.GetOrAdd(Normalize(providerKey), _ => new CircuitState());
        lock (state.SyncRoot)
        {
            if (state.OpenUntil is null)
            {
                return true;
            }

            if (state.OpenUntil <= now)
            {
                state.OpenUntil = null;
                state.ConsecutiveFailures = 0;
                return true;
            }

            return false;
        }
    }

    public void RecordSuccess(string providerKey)
    {
        var state = _states.GetOrAdd(Normalize(providerKey), _ => new CircuitState());
        lock (state.SyncRoot)
        {
            state.ConsecutiveFailures = 0;
            state.OpenUntil = null;
        }
    }

    public void RecordFailure(string providerKey, DateTimeOffset now, int failureThreshold, TimeSpan breakDuration)
    {
        var state = _states.GetOrAdd(Normalize(providerKey), _ => new CircuitState());
        lock (state.SyncRoot)
        {
            state.ConsecutiveFailures++;
            if (state.ConsecutiveFailures >= Math.Max(1, failureThreshold))
            {
                state.OpenUntil = now.Add(breakDuration);
            }
        }
    }

    private static string Normalize(string providerKey)
    {
        return string.IsNullOrWhiteSpace(providerKey) ? "default" : providerKey.Trim();
    }

    private sealed class CircuitState
    {
        public object SyncRoot { get; } = new();

        public int ConsecutiveFailures { get; set; }

        public DateTimeOffset? OpenUntil { get; set; }
    }
}
