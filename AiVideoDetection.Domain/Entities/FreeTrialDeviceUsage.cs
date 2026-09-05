namespace AiVideoDetection.Domain.Entities;

public class FreeTrialDeviceUsage
{
    public long Id { get; set; }
    public long DeviceIdentityId { get; set; }
    public DeviceIdentity DeviceIdentity { get; set; } = null!;
    public int AllocatedScans { get; set; } = 2;
    public int ConsumedScans { get; set; }
    public int ReservedScans { get; set; }
    public DateTimeOffset? FirstUsedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string ConcurrencyStamp { get; set; } = Guid.NewGuid().ToString("N");
}
