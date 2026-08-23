namespace AiVideoDetection.Application.Admin.DTOs;

public sealed class AdminDashboardSummaryDto
{
    public IReadOnlyList<AdminMetricDto> Metrics { get; init; } = [];

    public IReadOnlyList<AdminStatusCountDto> VideoStatuses { get; init; } = [];

    public IReadOnlyList<AdminStatusCountDto> JobStatuses { get; init; } = [];

    public IReadOnlyList<AdminStatusCountDto> RequestStatuses { get; init; } = [];

    public IReadOnlyList<AdminDailyActivityDto> UploadActivity { get; init; } = [];

    public IReadOnlyList<AdminDailyActivityDto> AnalysisActivity { get; init; } = [];

    public IReadOnlyList<AdminTopUserDto> TopUsers { get; init; } = [];

    public IReadOnlyList<AdminRecentActivityDto> RecentActivity { get; init; } = [];

    public AdminExternalRequestSummaryDto ExternalRequests { get; init; } = new();
}

public sealed class AdminMetricDto
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public long Value { get; init; }

    public string Tone { get; init; } = "default";
}

public sealed class AdminStatusCountDto
{
    public string Status { get; init; } = string.Empty;

    public long Count { get; init; }
}

public sealed class AdminDailyActivityDto
{
    public DateOnly Date { get; init; }

    public long Count { get; init; }
}

public sealed class AdminTopUserDto
{
    public long UserId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public long UploadCount { get; init; }
}

public sealed class AdminRecentActivityDto
{
    public string Type { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AdminExternalRequestSummaryDto
{
    public string ProviderName { get; init; } = "BitMind";

    public long TotalRequests { get; init; }

    public long PendingRequests { get; init; }

    public long CompletedRequests { get; init; }

    public long FailedRequests { get; init; }

    public int MonthlyQuotaLimit { get; init; }

    public int MonthlyUsed { get; init; }

    public int MonthlyRemaining { get; init; }

    public int MonthlySuccess { get; init; }

    public int MonthlyFailed { get; init; }
}

public sealed class AdminUserListItemDto
{
    public long UserId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public bool EmailConfirmed { get; init; }

    public long TotalVideos { get; init; }

    public long CompletedVideos { get; init; }

    public long FailedVideos { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class AdminUpdateUserStatusRequest
{
    public bool IsActive { get; init; }
}

public sealed class AdminVideoListItemDto
{
    public long VideoId { get; init; }

    public long UserId { get; init; }

    public string OwnerName { get; init; } = string.Empty;

    public string OwnerEmail { get; init; } = string.Empty;

    public string OriginalName { get; init; } = string.Empty;

    public long FileSize { get; init; }

    public string? ContentType { get; init; }

    public string Status { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string? LatestJobStatus { get; init; }

    public int? LatestJobProgress { get; init; }

    public string? FinalVerdict { get; init; }

    public decimal? AiGeneratedProbability { get; init; }

    public decimal? Confidence { get; init; }

    public bool ExternalVerificationUsed { get; init; }
}

public sealed class AdminVideoDetailDto
{
    public AdminVideoListItemDto Video { get; init; } = new();

    public AdminMetadataSummaryDto? Metadata { get; init; }

    public AdminAnalysisSummaryDto? Analysis { get; init; }

    public IReadOnlyList<AdminEvidenceItemDto> Evidence { get; init; } = [];

    public IReadOnlyList<AdminSourceMatchDto> OriginMatches { get; init; } = [];

    public IReadOnlyList<AdminJobListItemDto> Jobs { get; init; } = [];
}

public sealed class AdminMetadataSummaryDto
{
    public decimal? DurationSeconds { get; init; }

    public string? Resolution { get; init; }

    public decimal? Fps { get; init; }

    public string? Codec { get; init; }

    public string? AudioCodec { get; init; }

    public long? Bitrate { get; init; }

    public string? Encoder { get; init; }

    public DateTimeOffset? CreationTime { get; init; }

    public bool HasMissingMetadata { get; init; }
}

public sealed class AdminAnalysisSummaryDto
{
    public long AiResultId { get; init; }

    public string Label { get; init; } = string.Empty;

    public decimal FinalScore { get; init; }

    public decimal Confidence { get; init; }

    public decimal VisualScore { get; init; }

    public decimal? MetadataScore { get; init; }

    public decimal? TemporalScore { get; init; }

    public string? Summary { get; init; }

    public string Provider { get; init; } = string.Empty;

    public string ProviderMode { get; init; } = string.Empty;

    public string FinalDecisionSource { get; init; } = string.Empty;

    public string? ExternalProviderName { get; init; }

    public string? ExternalProviderStatus { get; init; }

    public bool FallbackUsed { get; init; }

    public string? FallbackReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class AdminEvidenceItemDto
{
    public string Type { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public decimal? ScoreImpact { get; init; }

    public decimal? TimestampSeconds { get; init; }
}

public sealed class AdminSourceMatchDto
{
    public string Platform { get; init; } = string.Empty;

    public string? Title { get; init; }

    public DateTimeOffset? UploadDatetime { get; init; }

    public decimal SimilarityScore { get; init; }

    public string Confidence { get; init; } = string.Empty;

    public int Rank { get; init; }
}

public sealed class AdminJobListItemDto
{
    public long JobId { get; init; }

    public long VideoId { get; init; }

    public string VideoName { get; init; } = string.Empty;

    public long UserId { get; init; }

    public string UserEmail { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int Progress { get; init; }

    public string? CurrentStep { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorCode { get; init; }

    public int RetryCount { get; init; }

    public int MaxRetryCount { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed class AdminProviderRequestListItemDto
{
    public long RequestId { get; init; }

    public long VideoId { get; init; }

    public string VideoName { get; init; } = string.Empty;

    public long UserId { get; init; }

    public string UserEmail { get; init; } = string.Empty;

    public string ProviderName { get; init; } = string.Empty;

    public string ProviderMode { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public int? HttpStatusCode { get; init; }

    public long? DurationMs { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset RequestStartedAt { get; init; }

    public DateTimeOffset? RequestCompletedAt { get; init; }
}

public sealed class AdminProviderRequestUserSummaryDto
{
    public long UserId { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public long TotalRequests { get; init; }

    public long PendingRequests { get; init; }

    public long CompletedRequests { get; init; }

    public long FailedRequests { get; init; }

    public DateTimeOffset? LatestRequestAt { get; init; }

    public string? LatestVideoName { get; init; }
}

public sealed class AdminVideoFileDto
{
    public string ObjectKey { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = "application/octet-stream";
}
