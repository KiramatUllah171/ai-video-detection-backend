namespace AiVideoDetection.Application.Videos.Options;

public class AiServiceOptions
{
    public const string SectionName = "AiService";

    public string BaseUrl { get; set; } = "http://localhost:8000";

    public string AnalyzeFramesPath { get; set; } = "/analyze-frames";

    public string AnalyzeVideoPath { get; set; } = "/analyze-video";

    public string HealthPath { get; set; } = "/health";

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxFramesPerRequest { get; set; } = 30;

    public string FrameSamplingStrategy { get; set; } = "uniform";

    public string ProviderMode { get; set; } =
        Environment.GetEnvironmentVariable("AI_PROVIDER")
        ?? Environment.GetEnvironmentVariable("PROVIDER_MODE")
        ?? "local";

    public bool BitMindEnabled { get; set; } = bool.TryParse(Environment.GetEnvironmentVariable("BITMIND_ENABLED"), out var enabled) && enabled;

    public int BitMindMonthlyQuota { get; set; } = int.TryParse(Environment.GetEnvironmentVariable("BITMIND_MONTHLY_QUOTA"), out var quota) ? quota : 100;

    public bool LocalFallbackEnabled { get; set; } = !bool.TryParse(Environment.GetEnvironmentVariable("LOCAL_FALLBACK_ENABLED"), out var fallback) || fallback;

    public string ExternalProviderPolicy { get; set; } = Environment.GetEnvironmentVariable("EXTERNAL_PROVIDER_POLICY") ?? "OnUncertain";
}
