using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AiVideoDetection.Api.Health;

public sealed class HangfireHealthCheck(JobStorage jobStorage) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var monitoringApi = jobStorage.GetMonitoringApi();
            var servers = monitoringApi.Servers();

            return Task.FromResult(servers.Count > 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy());
        }
        catch
        {
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }
    }
}
