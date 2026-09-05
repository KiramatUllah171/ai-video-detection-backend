namespace AiVideoDetection.Domain.Entities;

public class FreeTrialIpUsage
{
    public long Id { get; set; }
    public string IpHash { get; set; } = string.Empty;
    public string HashVersion { get; set; } = "hmac-sha256-v1";
    public int AllocatedScans { get; set; } = 2;
    public int ConsumedScans { get; set; }
    public int ReservedScans { get; set; }
    public DateTimeOffset? FirstUsedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
