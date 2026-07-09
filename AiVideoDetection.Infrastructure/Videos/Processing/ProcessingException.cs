namespace AiVideoDetection.Infrastructure.Videos.Processing;

public sealed class ProcessingException(string errorCode, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string ErrorCode { get; } = errorCode;

    public string SafeMessage { get; } = safeMessage;
}
