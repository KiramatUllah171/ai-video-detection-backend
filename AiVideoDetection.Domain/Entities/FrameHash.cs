namespace AiVideoDetection.Domain.Entities;

public class FrameHash
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public Video Video { get; set; } = null!;

    public long FrameId { get; set; }

    public VideoFrame VideoFrame { get; set; } = null!;

    public string? PHash { get; set; }

    public string? DHash { get; set; }

    public string? AHash { get; set; }

    public string HashVersion { get; set; } = "mvp-v1";

    public DateTimeOffset CreatedAt { get; set; }
}
