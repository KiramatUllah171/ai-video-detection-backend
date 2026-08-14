namespace AiVideoDetection.Application.Videos.DTOs;

public class AnalysisReportFile
{
    public string FileName { get; init; } = "ai-video-detection-report.pdf";

    public string ContentType { get; init; } = "application/pdf";

    public byte[] Content { get; init; } = [];
}
