using AiVideoDetection.Application.Videos.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Api.Health;

public sealed class ProviderConfigurationHealthCheck(IOptions<AiServiceOptions> options) : IHealthCheck
{
    private static readonly HashSet<string> AllowedProviderModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "local",
        "bitmind",
        "external",
        "fallbacklocal"
    };

    private static readonly HashSet<string> AllowedExternalProviderPolicies = new(StringComparer.OrdinalIgnoreCase)
    {
        "always",
        "onuncertain",
        "never"
    };

    private readonly AiServiceOptions _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var providerMode = (_options.ProviderMode ?? string.Empty).Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        var providerPolicy = (_options.ExternalProviderPolicy ?? string.Empty).Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(providerMode) || !AllowedProviderModes.Contains(providerMode))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }

        if (string.IsNullOrWhiteSpace(providerPolicy) || !AllowedExternalProviderPolicies.Contains(providerPolicy))
        {
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }

        if (_options.BitMindEnabled && _options.BitMindMonthlyQuota <= 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }

        return Task.FromResult(HealthCheckResult.Healthy());
    }
}
