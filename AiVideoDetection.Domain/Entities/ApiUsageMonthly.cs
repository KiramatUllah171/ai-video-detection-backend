namespace AiVideoDetection.Domain.Entities;

public class ApiUsageMonthly
{
    public long Id { get; set; }
    public string ProviderName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public int RequestCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int QuotaLimit { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
