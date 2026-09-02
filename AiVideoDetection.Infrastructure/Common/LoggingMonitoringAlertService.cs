using System.Collections.Concurrent;
using AiVideoDetection.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Common;

public sealed class LoggingMonitoringAlertService(
    IOptions<MonitoringOptions> options,
    ILogger<LoggingMonitoringAlertService> logger) : IMonitoringAlertService
{
    private readonly ConcurrentDictionary<string, AlertCounter> _counters = new(StringComparer.OrdinalIgnoreCase);
    private readonly MonitoringOptions _options = options.Value;

    public Task RecordFailedLoginAsync(
        string partitionKey,
        string? userEmail,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        return RecordOccurrenceAsync(
            "repeated-failed-login",
            partitionKey,
            LogLevel.Warning,
            "Repeated failed login attempts detected.",
            _options.FailedLoginThreshold,
            TimeSpan.FromMinutes(_options.FailedLoginWindowMinutes),
            new Dictionary<string, object?>
            {
                ["userEmail"] = userEmail,
                ["ipAddress"] = ipAddress
            });
    }

    public Task RecordApiErrorAsync(
        string partitionKey,
        string? path,
        int? statusCode,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        return RecordOccurrenceAsync(
            "repeated-api-errors",
            partitionKey,
            LogLevel.Error,
            "Repeated API errors detected.",
            _options.ApiErrorThreshold,
            TimeSpan.FromMinutes(_options.ApiErrorWindowMinutes),
            new Dictionary<string, object?>
            {
                ["path"] = path,
                ["statusCode"] = statusCode,
                ["correlationId"] = correlationId
            });
    }

    public Task RecordProviderFailureAsync(
        string providerName,
        string? reason,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        return RecordOccurrenceAsync(
            "provider-failures",
            providerName,
            LogLevel.Error,
            "Repeated provider failures detected.",
            _options.ProviderFailureThreshold,
            TimeSpan.FromMinutes(_options.ProviderFailureWindowMinutes),
            new Dictionary<string, object?>
            {
                ["providerName"] = providerName,
                ["reason"] = reason,
                ["correlationId"] = correlationId
            });
    }

    public Task RecordProviderQuotaReachedAsync(
        string providerName,
        int requestCount,
        int quotaLimit,
        string? correlationId,
        CancellationToken cancellationToken = default)
    {
        LogAlert(
            "provider-quota-reached",
            providerName,
            LogLevel.Warning,
            "Provider quota reached.",
            1,
            1,
            new Dictionary<string, object?>
            {
                ["providerName"] = providerName,
                ["requestCount"] = requestCount,
                ["quotaLimit"] = quotaLimit,
                ["correlationId"] = correlationId
            });
        return Task.CompletedTask;
    }

    public Task RecordCleanupFailureAsync(
        string cleanupArea,
        string? target,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return RecordOccurrenceAsync(
            "cleanup-failures",
            cleanupArea,
            LogLevel.Error,
            "Retention cleanup failures detected.",
            _options.CleanupFailureThreshold,
            TimeSpan.FromMinutes(_options.CleanupFailureWindowMinutes),
            new Dictionary<string, object?>
            {
                ["cleanupArea"] = cleanupArea,
                ["target"] = target,
                ["reason"] = reason
            });
    }

    private Task RecordOccurrenceAsync(
        string alertName,
        string partitionKey,
        LogLevel logLevel,
        string message,
        int threshold,
        TimeSpan window,
        IReadOnlyDictionary<string, object?> metadata)
    {
        var normalizedPartitionKey = string.IsNullOrWhiteSpace(partitionKey) ? "unknown" : partitionKey.Trim();
        var counterKey = $"{alertName}:{normalizedPartitionKey}";
        var now = DateTimeOffset.UtcNow;
        var counter = _counters.GetOrAdd(counterKey, _ => new AlertCounter(now));
        var shouldAlert = false;
        var count = 0;

        lock (counter.SyncRoot)
        {
            if (now - counter.WindowStartedAt > window)
            {
                counter.WindowStartedAt = now;
                counter.Count = 0;
                counter.LastAlertAt = null;
            }

            counter.Count++;
            count = counter.Count;

            var suppressionWindow = TimeSpan.FromMinutes(Math.Max(1, _options.AlertSuppressionMinutes));
            if (counter.Count >= Math.Max(1, threshold)
                && (counter.LastAlertAt is null || now - counter.LastAlertAt >= suppressionWindow))
            {
                counter.LastAlertAt = now;
                shouldAlert = true;
            }
        }

        if (shouldAlert)
        {
            LogAlert(alertName, normalizedPartitionKey, logLevel, message, count, threshold, metadata);
        }

        return Task.CompletedTask;
    }

    private void LogAlert(
        string alertName,
        string partitionKey,
        LogLevel logLevel,
        string message,
        int count,
        int threshold,
        IReadOnlyDictionary<string, object?> metadata)
    {
        logger.Log(
            logLevel,
            "Monitoring alert {AlertName} for {PartitionKey}: {Message} Count={Count} Threshold={Threshold} Metadata={@Metadata}",
            alertName,
            partitionKey,
            message,
            count,
            threshold,
            metadata);
    }

    private sealed class AlertCounter(DateTimeOffset windowStartedAt)
    {
        public object SyncRoot { get; } = new();

        public DateTimeOffset WindowStartedAt { get; set; } = windowStartedAt;

        public int Count { get; set; }

        public DateTimeOffset? LastAlertAt { get; set; }
    }
}
