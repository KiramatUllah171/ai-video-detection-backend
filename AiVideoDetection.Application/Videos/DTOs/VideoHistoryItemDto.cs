namespace AiVideoDetection.Application.Videos.DTOs;

public class VideoHistoryItemDto
{
    public long VideoId { get; init; }

    public string OriginalName { get; init; } = string.Empty;

    public long FileSize { get; init; }

    public string? ContentType { get; init; }

    public string? FileExtension { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public long? LatestJobId { get; init; }

    public string? LatestJobStatus { get; init; }

    public int? LatestJobProgress { get; init; }

    public string? CurrentStep { get; init; }
}
