namespace AiVideoDetection.Domain.Enums;

public enum JobStatus
{
    Queued,
    Processing,
    Completed,
    Failed,
    Retrying,
    Cancelled
}
