namespace AiVideoDetection.Application.Subscriptions.Interfaces;

public interface IClientIpResolver
{
    string? GetClientIpAddress();
}
