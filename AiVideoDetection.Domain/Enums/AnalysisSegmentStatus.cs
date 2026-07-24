namespace AiVideoDetection.Domain.Enums;

public enum AnalysisSegmentStatus
{
    Pending,
    Preparing,
    Ready,
    Analyzing,
    Completed,
    Failed,
    CancelRequested,
    Cancelled
}
