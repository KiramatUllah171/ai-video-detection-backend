using AiVideoDetection.Api.Health;
using AiVideoDetection.Application.Videos.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Api;

public class ProviderConfigurationHealthCheckTests
{
    [Fact]
    public async Task HybridProviderModeIsHealthy()
    {
        var result = await CheckAsync(new AiServiceOptions
        {
            ProviderMode = "hybrid",
            ExternalProviderPolicy = "OnUncertain",
            BitMindEnabled = true,
            BitMindMonthlyQuota = 100
        });

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Theory]
    [InlineData("Disabled")]
    [InlineData("Never")]
    public async Task DisabledExternalProviderPolicyAliasesAreHealthy(string policy)
    {
        var result = await CheckAsync(new AiServiceOptions
        {
            ProviderMode = "local",
            ExternalProviderPolicy = policy,
            BitMindEnabled = false,
            BitMindMonthlyQuota = 100
        });

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    private static Task<HealthCheckResult> CheckAsync(AiServiceOptions options)
    {
        var healthCheck = new ProviderConfigurationHealthCheck(Options.Create(options));
        return healthCheck.CheckHealthAsync(new HealthCheckContext());
    }
}
