using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Subscriptions.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace AiVideoDetection.Infrastructure.Subscriptions;

public class DeviceIdentityService(
    AppDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IHashPseudonymizationService hashService,
    IClientIpResolver clientIpResolver,
    IOptions<SubscriptionSecurityOptions> options,
    IHostEnvironment environment) : IDeviceIdentityService
{
    private readonly SubscriptionSecurityOptions _options = options.Value;

    public async Task<SubscriptionClientContext> ResolveAsync(long userId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var device = await ResolveDeviceAsync(now, cancellationToken);

        var link = await dbContext.AccountDeviceLinks
            .FirstOrDefaultAsync(
                accountDeviceLink => accountDeviceLink.UserId == userId && accountDeviceLink.DeviceIdentityId == device.Id,
                cancellationToken);

        if (link is null)
        {
            dbContext.AccountDeviceLinks.Add(new AccountDeviceLink
            {
                UserId = userId,
                DeviceIdentityId = device.Id,
                FirstSeenAt = now,
                LastSeenAt = now
            });
        }
        else
        {
            link.LastSeenAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return BuildClientContext(device);
    }

    public async Task<SubscriptionClientContext> ResolveAnonymousAsync(CancellationToken cancellationToken = default)
    {
        var device = await ResolveDeviceAsync(DateTimeOffset.UtcNow, cancellationToken);
        return BuildClientContext(device);
    }

    private async Task<DeviceIdentity> ResolveDeviceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext;
        var deviceToken = GetOrCreateDeviceToken(httpContext);
        var deviceTokenHash = hashService.HashValue("device-token", deviceToken);
        var fingerprintHash = TryGetFingerprintHash(httpContext);

        var device = await dbContext.DeviceIdentities
            .FirstOrDefaultAsync(identity => identity.DeviceTokenHash == deviceTokenHash, cancellationToken);

        if (device is null)
        {
            device = new DeviceIdentity
            {
                DeviceTokenHash = deviceTokenHash,
                DeviceFingerprintHash = fingerprintHash,
                HashVersion = hashService.HashVersion,
                FirstSeenAt = now,
                LastSeenAt = now
            };
            dbContext.DeviceIdentities.Add(device);
        }
        else
        {
            device.DeviceTokenHash ??= deviceTokenHash;
            device.DeviceFingerprintHash ??= fingerprintHash;
            device.LastSeenAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return device;
    }

    private SubscriptionClientContext BuildClientContext(DeviceIdentity device)
    {
        var ipAddress = clientIpResolver.GetClientIpAddress();
        return new SubscriptionClientContext
        {
            DeviceIdentityId = device.Id,
            IpAddress = ipAddress,
            IpHash = string.IsNullOrWhiteSpace(ipAddress) ? null : hashService.HashValue("client-ip", ipAddress)
        };
    }

    private string GetOrCreateDeviceToken(HttpContext? httpContext)
    {
        if (httpContext?.Request.Cookies.TryGetValue(_options.DeviceCookieName, out var existingToken) == true &&
            IsPlausibleDeviceToken(existingToken))
        {
            return existingToken;
        }

        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        if (httpContext is not null)
        {
            httpContext.Response.Cookies.Append(
                _options.DeviceCookieName,
                token,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = !environment.IsDevelopment() || _options.RequireSecureDeviceCookieInDevelopment,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddDays(Math.Clamp(_options.DeviceCookieDays, 1, 730))
                });
        }

        return token;
    }

    private string? TryGetFingerprintHash(HttpContext? httpContext)
    {
        if (httpContext is null ||
            string.IsNullOrWhiteSpace(_options.DeviceFingerprintHeaderName) ||
            !httpContext.Request.Headers.TryGetValue(_options.DeviceFingerprintHeaderName, out var headerValues))
        {
            return null;
        }

        var fingerprint = headerValues.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        fingerprint = fingerprint.Trim();
        if (fingerprint.Length > 512)
        {
            fingerprint = fingerprint[..512];
        }

        return hashService.HashValue("device-fingerprint", fingerprint);
    }

    private static bool IsPlausibleDeviceToken(string value)
    {
        return value.Length is >= 32 and <= 256;
    }
}
