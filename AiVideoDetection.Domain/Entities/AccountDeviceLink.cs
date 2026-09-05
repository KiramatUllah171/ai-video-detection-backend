namespace AiVideoDetection.Domain.Entities;

public class AccountDeviceLink
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long DeviceIdentityId { get; set; }
    public DeviceIdentity DeviceIdentity { get; set; } = null!;
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
