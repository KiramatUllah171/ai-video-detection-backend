using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Domain.Entities;

public class AnalysisJob
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public Video Video { get; set; } = null!;

    public JobStatus Status { get; set; } = JobStatus.Queued;

    public int Progress { get; set; }

    public string? CurrentStep { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ErrorCode { get; set; }

    public string? FailedStage { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public bool CancelRequested { get; set; }

    public DateTimeOffset? CancelRequestedAt { get; set; }

    public bool PauseRequested { get; set; }

    public DateTimeOffset? PauseRequestedAt { get; set; }

    public DateTimeOffset? PausedAt { get; set; }

    public DateTimeOffset? ResumedAt { get; set; }

    public string? PausedFromStage { get; set; }

    public string? LastCheckpoint { get; set; }

    public string? ResumeBackgroundJobId { get; set; }

    public string? ScanMode { get; set; }

    public int CompletedSegments { get; set; }

    public int TotalSegments { get; set; }

    public decimal? AnalyzedCoverageSeconds { get; set; }

    public decimal? TotalDurationSeconds { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }

    public int RetryCount { get; set; }

    public int MaxRetryCount { get; set; } = 3;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<JobLog> Logs { get; set; } = [];

    public ICollection<AnalysisSegment> Segments { get; set; } = [];
}
