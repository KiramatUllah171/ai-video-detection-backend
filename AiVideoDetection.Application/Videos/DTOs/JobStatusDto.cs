namespace AiVideoDetection.Application.Videos.DTOs;

public class JobStatusDto
{
    public long JobId { get; init; }

    public long VideoId { get; init; }

    public string Status { get; init; } = string.Empty;

    public int Progress { get; init; }

    public string? CurrentStep { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorCode { get; init; }

    public int RetryCount { get; init; }

    public int MaxRetryCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}
