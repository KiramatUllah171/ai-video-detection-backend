namespace AiVideoDetection.Domain.Entities;

public class JobLog
{
    public long Id { get; set; }

    public long JobId { get; set; }

    public AnalysisJob AnalysisJob { get; set; } = null!;

    public string StepName { get; set; } = string.Empty;

    public string Level { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? DetailsJson { get; set; }

    public string? CorrelationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
