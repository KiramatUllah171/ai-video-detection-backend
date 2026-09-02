using AiVideoDetection.Application.Videos.Interfaces;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AiVideoDetection.Api.Health;

public sealed class AiServiceHealthCheck(IAiInferenceClient aiInferenceClient) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

            return await aiInferenceClient.HealthCheckAsync(timeoutCts.Token)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
