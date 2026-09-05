using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.Options;
using AiVideoDetection.Infrastructure.Subscriptions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Subscriptions;

public class HmacPseudonymizationServiceTests
{
    [Fact]
    public void HashValueReturnsStablePurposeScopedHash()
    {
        var service = new HmacPseudonymizationService(Options.Create(new SubscriptionSecurityOptions
        {
            HmacSecret = "01234567890123456789012345678901"
        }));

        var first = service.HashValue("client-ip", "203.0.113.10");
        var second = service.HashValue("client-ip", "203.0.113.10");
        var differentPurpose = service.HashValue("device-token", "203.0.113.10");

        Assert.Equal(first, second);
        Assert.NotEqual(first, differentPurpose);
        Assert.Equal(64, first.Length);
    }

    [Fact]
    public void HashValueRejectsMissingSecret()
    {
        var service = new HmacPseudonymizationService(Options.Create(new SubscriptionSecurityOptions()));

        var exception = Assert.Throws<InvalidOperationException>(() => service.HashValue("client-ip", "203.0.113.10"));

        Assert.Equal(SubscriptionErrorCodes.SubscriptionSecurityNotConfigured, exception.Message);
    }
}
