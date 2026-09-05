namespace AiVideoDetection.Domain.Entities;

public class SubscriptionPlan
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal PriceAmount { get; set; }
    public string Currency { get; set; } = "PKR";
    public int ScanLimit { get; set; }
    public long MaxVideoSizeBytes { get; set; }
    public bool AllowsSmartScan { get; set; }
    public bool AllowsDetailedScan { get; set; }
    public int? ValidityDays { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
