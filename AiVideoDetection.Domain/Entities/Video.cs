using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Domain.Entities;

public class Video
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User User { get; set; } = null!;

    public string OriginalName { get; set; } = string.Empty;

    public string FileUrl { get; set; } = string.Empty;

    public string? ThumbnailUrl { get; set; }

    public string? ContentType { get; set; }

    public string? FileExtension { get; set; }

    public long FileSize { get; set; }

    public decimal? DurationSeconds { get; set; }

    public string? FormatName { get; set; }

    public string? Sha256Hash { get; set; }

    public VideoStatus Status { get; set; } = VideoStatus.Uploaded;

    public DateTimeOffset? RetentionDeleteAt { get; set; }

    public string? GuestAccessTokenHash { get; set; }

    public DateTimeOffset? GuestAccessExpiresAt { get; set; }

    public DateTimeOffset? GuestClaimedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AnalysisJob> AnalysisJobs { get; set; } = [];

    public ICollection<VideoFrame> Frames { get; set; } = [];

    public MetadataResult? MetadataResult { get; set; }

    public ICollection<AiResult> AiResults { get; set; } = [];

    public ICollection<FrameHash> FrameHashes { get; set; } = [];

    public ICollection<SourceMatch> SourceMatches { get; set; } = [];
}
