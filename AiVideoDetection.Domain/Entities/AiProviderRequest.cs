namespace AiVideoDetection.Domain.Entities;

public class AiProviderRequest
{
    public long Id { get; set; }
    public long VideoId { get; set; }
    public Video Video { get; set; } = null!;
    public long AnalysisJobId { get; set; }
    public AnalysisJob AnalysisJob { get; set; } = null!;
    public long? AiResultId { get; set; }
    public AiResult? AiResult { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public string ProviderName { get; set; } = string.Empty;
    public string ProviderMode { get; set; } = string.Empty;
    public string? ProviderRequestId { get; set; }
    public string? ProviderJobId { get; set; }
    public string Status { get; set; } = "Started";
    public DateTimeOffset RequestStartedAt { get; set; }
    public DateTimeOffset? RequestCompletedAt { get; set; }
    public long? DurationMs { get; set; }
    public int? HttpStatusCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RawRequestMetadataJson { get; set; }
    public string? RawResponseJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
