using System.Text.Json;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Data;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class JobLogService(
    AppDbContext dbContext,
    ICorrelationIdAccessor correlationIdAccessor,
    IMonitoringAlertService monitoringAlertService) : IJobLogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task LogAsync(
        long jobId,
        string stepName,
        string level,
        string message,
        object? details = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = correlationIdAccessor.CorrelationId;
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
            correlationIdAccessor.CorrelationId = correlationId;
        }

        var detailsJson = details is null ? null : JsonSerializer.Serialize(details, SerializerOptions);
        dbContext.JobLogs.Add(new JobLog
        {
            JobId = jobId,
            StepName = stepName,
            Level = level,
            Message = message,
            DetailsJson = detailsJson,
            CorrelationId = correlationId
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await RecordMonitoringSignalsAsync(stepName, message, detailsJson, correlationId, cancellationToken);
    }

    private Task RecordMonitoringSignalsAsync(
        string stepName,
        string message,
        string? detailsJson,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (stepName.Equals("ProviderRequestFailed", StringComparison.OrdinalIgnoreCase))
        {
            return monitoringAlertService.RecordProviderFailureAsync(
                ResolveProviderName(detailsJson),
                message,
                correlationId,
                cancellationToken);
        }

        if (stepName.Equals("ExternalProviderSkipped", StringComparison.OrdinalIgnoreCase)
            && message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            var usage = ResolveQuotaUsage(detailsJson);
            return monitoringAlertService.RecordProviderQuotaReachedAsync(
                "BitMind",
                usage.RequestCount,
                usage.QuotaLimit,
                correlationId,
                cancellationToken);
        }

        return Task.CompletedTask;
    }

    private static string ResolveProviderName(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return "Provider";
        }

        if (detailsJson.Contains("bitmind", StringComparison.OrdinalIgnoreCase))
        {
            return "BitMind";
        }

        if (detailsJson.Contains("local", StringComparison.OrdinalIgnoreCase))
        {
            return "Internal";
        }

        return "Provider";
    }

    private static (int RequestCount, int QuotaLimit) ResolveQuotaUsage(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return (0, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            var root = document.RootElement;
            var requestCount = root.TryGetProperty("requestCount", out var requestCountElement) && requestCountElement.TryGetInt32(out var parsedRequestCount)
                ? parsedRequestCount
                : 0;
            var quotaLimit = root.TryGetProperty("quotaLimit", out var quotaLimitElement) && quotaLimitElement.TryGetInt32(out var parsedQuotaLimit)
                ? parsedQuotaLimit
                : 0;
            return (requestCount, quotaLimit);
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }
}
