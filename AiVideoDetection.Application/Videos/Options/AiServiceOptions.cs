namespace AiVideoDetection.Application.Videos.Options;

public class AiServiceOptions
{
    public const string SectionName = "AiService";

    public string BaseUrl { get; set; } = "http://localhost:8000";

    public string AnalyzeFramesPath { get; set; } = "/analyze-frames";

    public string HealthPath { get; set; } = "/health";

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxFramesPerRequest { get; set; } = 30;
}
