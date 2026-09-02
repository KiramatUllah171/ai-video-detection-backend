namespace AiVideoDetection.Api.Options;

public sealed class ApplicationDataProtectionOptions
{
    public const string SectionName = "DataProtection";

    public string ApplicationName { get; init; } = "SachAI";

    public string? KeyRingPath { get; init; }
}
