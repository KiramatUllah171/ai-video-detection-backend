namespace AiVideoDetection.Domain.Enums;

public enum JobStatus
{
    Queued,
    Preparing,
    Processing,
    Finalizing,
    PauseRequested,
    Paused,
    ResumeRequested,
    Completed,
    Failed,
    Retrying,
    CancelRequested,
    Cancelled
}
