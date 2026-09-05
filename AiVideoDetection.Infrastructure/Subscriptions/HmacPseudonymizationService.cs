using System.Security.Cryptography;
using System.Text;
using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Subscriptions.Options;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Subscriptions;

public class HmacPseudonymizationService(IOptions<SubscriptionSecurityOptions> options) : IHashPseudonymizationService
{
    public string HashVersion => "hmac-sha256-v1";

    public string HashValue(string purpose, string value)
    {
        if (string.IsNullOrWhiteSpace(options.Value.HmacSecret) || options.Value.HmacSecret.Length < 32)
        {
            throw new InvalidOperationException(SubscriptionErrorCodes.SubscriptionSecurityNotConfigured);
        }

        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("Hash purpose is required.", nameof(purpose));
        }

        var normalizedValue = value.Trim();
        var payload = $"{purpose.Trim()}:{normalizedValue}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.Value.HmacSecret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}
