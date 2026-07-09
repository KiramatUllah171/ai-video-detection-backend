namespace AiVideoDetection.Infrastructure.Videos.Ai;

public sealed class AiServiceException(string errorCode, string safeMessage, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string ErrorCode { get; } = errorCode;

    public string SafeMessage { get; } = safeMessage;
}
