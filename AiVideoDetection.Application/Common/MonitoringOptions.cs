namespace AiVideoDetection.Application.Common;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public int AlertSuppressionMinutes { get; set; } = 15;

    public int FailedLoginThreshold { get; set; } = 10;

    public int FailedLoginWindowMinutes { get; set; } = 10;

    public int ApiErrorThreshold { get; set; } = 5;

    public int ApiErrorWindowMinutes { get; set; } = 5;

    public int ProviderFailureThreshold { get; set; } = 3;

    public int ProviderFailureWindowMinutes { get; set; } = 10;

    public int CleanupFailureThreshold { get; set; } = 1;

    public int CleanupFailureWindowMinutes { get; set; } = 60;

    public int ProductionLogRetentionDays { get; set; } = 90;
}
