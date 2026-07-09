namespace AiVideoDetection.Domain.Entities;

public class MetadataResult
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public Video Video { get; set; } = null!;

    public string? Codec { get; set; }

    public string? AudioCodec { get; set; }

    public decimal? Fps { get; set; }

    public string? Resolution { get; set; }

    public decimal? DurationSeconds { get; set; }

    public long? Bitrate { get; set; }

    public string? Encoder { get; set; }

    public DateTimeOffset? CreationTime { get; set; }

    public bool HasMissingMetadata { get; set; }

    public string? WarningsJson { get; set; }

    public string? RawJson { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
