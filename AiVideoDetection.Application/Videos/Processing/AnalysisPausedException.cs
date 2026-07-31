namespace AiVideoDetection.Application.Videos.Processing;

public class AnalysisPausedException(string safeMessage) : Exception(safeMessage)
{
    public string SafeMessage { get; } = safeMessage;
}
