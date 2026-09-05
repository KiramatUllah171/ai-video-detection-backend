using System.Net;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using Microsoft.AspNetCore.Http;

namespace AiVideoDetection.Infrastructure.Subscriptions;

public class ClientIpResolver(IHttpContextAccessor httpContextAccessor) : IClientIpResolver
{
    public string? GetClientIpAddress()
    {
        var remoteIpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress;
        if (remoteIpAddress is null)
        {
            return null;
        }

        if (remoteIpAddress.IsIPv4MappedToIPv6)
        {
            remoteIpAddress = remoteIpAddress.MapToIPv4();
        }

        if (IPAddress.IsLoopback(remoteIpAddress))
        {
            return remoteIpAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Loopback.ToString()
                : IPAddress.Loopback.ToString();
        }

        return remoteIpAddress.ToString();
    }
}
