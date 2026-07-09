namespace AiVideoDetection.Application.Videos.DTOs;

public class VideoFrameDto
{
    public long Id { get; init; }

    public long VideoId { get; init; }

    public string FrameUrl { get; init; } = string.Empty;

    public decimal TimestampSeconds { get; init; }

    public int FrameIndex { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public bool IsKeyframe { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
