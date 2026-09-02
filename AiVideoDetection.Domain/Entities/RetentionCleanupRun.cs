namespace AiVideoDetection.Domain.Entities;

public class RetentionCleanupRun
{
    public long Id { get; set; }

    public string JobName { get; set; } = string.Empty;

    public string Status { get; set; } = "Succeeded";

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public long DurationMs { get; set; }

    public int WorkDirectoriesDeleted { get; set; }

    public int FrameObjectsCleared { get; set; }

    public int OriginalVideosCleared { get; set; }

    public int ThumbnailsCleared { get; set; }

    public int EvidenceRowsDeleted { get; set; }

    public int SourceMatchRowsDeleted { get; set; }

    public int ProviderPayloadsCleared { get; set; }

    public int AnalysisPayloadsCleared { get; set; }

    public int SegmentPayloadsCleared { get; set; }

    public int FailureCount { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
