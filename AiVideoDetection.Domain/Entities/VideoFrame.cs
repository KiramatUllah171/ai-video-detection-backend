namespace AiVideoDetection.Domain.Entities;

public class VideoFrame
{
    public long Id { get; set; }

    public long VideoId { get; set; }

    public Video Video { get; set; } = null!;

    public string FrameUrl { get; set; } = string.Empty;

    public decimal TimestampSeconds { get; set; }

    public int FrameIndex { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public bool IsKeyframe { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<EvidenceItem> EvidenceItems { get; set; } = [];
}
