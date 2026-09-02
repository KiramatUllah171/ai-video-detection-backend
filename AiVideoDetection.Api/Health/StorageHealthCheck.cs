using AiVideoDetection.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Api.Health;

public sealed class StorageHealthCheck(IOptions<LocalStorageOptions> options) : IHealthCheck
{
    private readonly LocalStorageOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(_options.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return HealthCheckResult.Healthy();
        }

        try
        {
            var rootPath = Path.GetFullPath(_options.LocalRootPath);
            Directory.CreateDirectory(rootPath);

            var healthDirectory = Path.Combine(rootPath, ".health");
            Directory.CreateDirectory(healthDirectory);

            var probePath = Path.Combine(healthDirectory, $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);

            return HealthCheckResult.Healthy();
        }
        catch
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
