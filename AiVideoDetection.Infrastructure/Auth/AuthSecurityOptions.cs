namespace AiVideoDetection.Infrastructure.Auth;

public sealed class AuthSecurityOptions
{
    public const string SectionName = "AuthSecurity";

    public bool LockoutEnabled { get; init; } = true;

    public int MaxFailedAccessAttempts { get; init; } = 5;

    public int LockoutMinutes { get; init; } = 15;

    public int EmailThrottleWindowMinutes { get; init; } = 15;

    public int LoginEmailPermitLimit { get; init; } = 10;

    public int PasswordResetEmailPermitLimit { get; init; } = 3;

    public int EmailConfirmationResendPermitLimit { get; init; } = 3;

    public int ReportDownloadPerReportPermitLimit { get; init; } = 10;
}
