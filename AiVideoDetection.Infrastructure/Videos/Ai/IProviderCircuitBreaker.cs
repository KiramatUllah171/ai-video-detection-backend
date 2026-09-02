namespace AiVideoDetection.Infrastructure.Videos.Ai;

public interface IProviderCircuitBreaker
{
    ProviderCircuitSnapshot GetSnapshot(string providerKey);

    bool CanExecute(string providerKey, DateTimeOffset now);

    void RecordSuccess(string providerKey);

    void RecordFailure(string providerKey, DateTimeOffset now, int failureThreshold, TimeSpan breakDuration);
}

public sealed record ProviderCircuitSnapshot(
    bool IsOpen,
    int ConsecutiveFailures,
    DateTimeOffset? OpenUntil);
