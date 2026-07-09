namespace AiVideoDetection.Application.Videos.Processing;

public sealed record ExtractedMetadataResult(
    string? FormatName,
    string? Codec,
    string? AudioCodec,
    decimal? Fps,
    string? Resolution,
    decimal? DurationSeconds,
    long? Bitrate,
    string? Encoder,
    DateTimeOffset? CreationTime,
    bool HasMissingMetadata,
    IReadOnlyList<string> Warnings,
    string RawJson);
