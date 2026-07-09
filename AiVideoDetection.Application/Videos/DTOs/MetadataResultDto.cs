namespace AiVideoDetection.Application.Videos.DTOs;

public class MetadataResultDto
{
    public long VideoId { get; init; }

    public string? Codec { get; init; }

    public string? AudioCodec { get; init; }

    public decimal? Fps { get; init; }

    public string? Resolution { get; init; }

    public decimal? DurationSeconds { get; init; }

    public long? Bitrate { get; init; }

    public string? Encoder { get; init; }

    public DateTimeOffset? CreationTime { get; init; }

    public bool HasMissingMetadata { get; init; }

    public string? WarningsJson { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}
