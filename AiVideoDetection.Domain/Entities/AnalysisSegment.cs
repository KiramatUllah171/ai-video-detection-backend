using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Domain.Entities;

public class AnalysisSegment
{
    public long Id { get; set; }

    public long AnalysisJobId { get; set; }

    public AnalysisJob AnalysisJob { get; set; } = null!;

    public long VideoId { get; set; }

    public Video Video { get; set; } = null!;

    public int SegmentIndex { get; set; }

    public decimal StartTime { get; set; }

    public decimal EndTime { get; set; }

    public decimal Duration { get; set; }

    public string? LocalTemporaryPath { get; set; }

    public AnalysisSegmentStatus Status { get; set; } = AnalysisSegmentStatus.Pending;

    public int Progress { get; set; }

    public int AttemptCount { get; set; }

    public string? ProviderRequestId { get; set; }

    public decimal? AiScore { get; set; }

    public decimal? Confidence { get; set; }

    public string? ResultJson { get; set; }

    public string? ErrorCode { get; set; }

    public string? SafeErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public DateTimeOffset? LastActivityAt { get; set; }
}
