namespace AiVideoDetection.Application.Subscriptions.DTOs;

public class FreeTrialStatusDto
{
    public int AccountRemainingScans { get; init; }
    public int? DeviceRemainingScans { get; init; }
    public int? IpRemainingScans { get; init; }
    public int EffectiveRemainingScans { get; init; }
}
