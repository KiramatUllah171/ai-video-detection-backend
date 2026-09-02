namespace AiVideoDetection.Api.Options;

public sealed class DeploymentReadinessOptions
{
    public const string SectionName = "DeploymentReadiness";

    public bool RequireHttps { get; init; } = true;

    public bool DatabaseBackupConfigured { get; init; }

    public bool ReportStorageBackupConfigured { get; init; }

    public bool MigrationReleaseProcessConfigured { get; init; }

    public bool HealthChecksConnected { get; init; }

    public bool MonitoringDashboardConfigured { get; init; }

    public string? MonitoringDashboardUrl { get; init; }
}
