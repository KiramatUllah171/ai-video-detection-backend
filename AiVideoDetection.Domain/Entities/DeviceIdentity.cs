namespace AiVideoDetection.Domain.Entities;

public class DeviceIdentity
{
    public long Id { get; set; }
    public string? DeviceTokenHash { get; set; }
    public string? DeviceFingerprintHash { get; set; }
    public string HashVersion { get; set; } = "hmac-sha256-v1";
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public string RiskStatus { get; set; } = "Normal";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
