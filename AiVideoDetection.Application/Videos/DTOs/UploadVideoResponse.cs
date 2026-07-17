namespace AiVideoDetection.Application.Videos.DTOs;

public class UploadVideoResponse
{
    public long VideoId { get; init; }

    public long JobId { get; init; }

    public string Status { get; init; } = string.Empty;

    public string JobStatus { get; init; } = string.Empty;

    public string OriginalName { get; init; } = string.Empty;

    public long FileSize { get; init; }

    public string ContentType { get; init; } = string.Empty;

    public int RetryCount { get; init; }

    public int MaxRetryCount { get; init; }

    public string Message { get; init; } = string.Empty;
}
