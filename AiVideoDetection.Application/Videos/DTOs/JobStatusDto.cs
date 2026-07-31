namespace AiVideoDetection.Application.Videos.DTOs;

public class JobStatusDto
{
    public long JobId { get; init; }

    public long VideoId { get; init; }

    public string OriginalName { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int Progress { get; init; }

    public string? CurrentStep { get; init; }

    public string? LastCheckpoint { get; init; }

    public string? ScanMode { get; init; }

    public int CompletedSegments { get; init; }

    public int TotalSegments { get; init; }

    public decimal? AnalyzedCoverageSeconds { get; init; }

    public decimal? TotalDurationSeconds { get; init; }

    public DateTimeOffset? LastActivityAt { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorCode { get; init; }

    public string? UserMessage { get; init; }

    public bool CanRetry { get; init; }

    public int RetryCount { get; init; }

    public int MaxRetryCount { get; init; }

    public string? FailedStage { get; init; }

    public string? NextRecommendedAction { get; init; }

    public string? TechnicalReferenceId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset LastUpdatedAt { get; init; }
}
