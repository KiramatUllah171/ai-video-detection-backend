namespace AiVideoDetection.Application.Videos.DTOs;

public class VideoDetailDto
{
    public long VideoId { get; init; }

    public string OriginalName { get; init; } = string.Empty;

    public string? ContentType { get; init; }

    public string? FileExtension { get; init; }

    public long FileSize { get; init; }

    public decimal? DurationSeconds { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public JobStatusDto? LatestJob { get; init; }
}
