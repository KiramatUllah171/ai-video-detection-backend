namespace AiVideoDetection.Application.Common;

public interface IMonitoringAlertService
{
    Task RecordFailedLoginAsync(
        string partitionKey,
        string? userEmail,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task RecordApiErrorAsync(
        string partitionKey,
        string? path,
        int? statusCode,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task RecordProviderFailureAsync(
        string providerName,
        string? reason,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task RecordProviderQuotaReachedAsync(
        string providerName,
        int requestCount,
        int quotaLimit,
        string? correlationId,
        CancellationToken cancellationToken = default);

    Task RecordCleanupFailureAsync(
        string cleanupArea,
        string? target,
        string? reason,
        CancellationToken cancellationToken = default);
}
