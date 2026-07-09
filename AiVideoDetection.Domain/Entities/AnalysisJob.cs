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

    public int RetryCount { get; set; }

    public int MaxRetryCount { get; set; } = 3;

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<JobLog> Logs { get; set; } = [];
}
