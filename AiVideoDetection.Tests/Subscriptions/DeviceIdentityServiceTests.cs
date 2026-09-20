using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Subscriptions.Options;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Subscriptions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Subscriptions;

public class DeviceIdentityServiceTests
{
    [Fact]
    public async Task ResolveAnonymousAsyncDoesNotMergeDifferentVisitorsBySharedFingerprint()
    {
        await using var dbContext = CreateDbContext();
        var httpContextAccessor = new HttpContextAccessor();
        var service = CreateService(dbContext, httpContextAccessor);

        httpContextAccessor.HttpContext = CreateHttpContext("shared-browser-fingerprint");
        var first = await service.ResolveAnonymousAsync();
        httpContextAccessor.HttpContext = CreateHttpContext("shared-browser-fingerprint");
        var second = await service.ResolveAnonymousAsync();

        Assert.NotEqual(first.DeviceIdentityId, second.DeviceIdentityId);
        Assert.Equal(2, await dbContext.DeviceIdentities.CountAsync());
        Assert.Equal(2, await dbContext.DeviceIdentities.Select(device => device.DeviceTokenHash).Distinct().CountAsync());
        Assert.Equal(1, await dbContext.DeviceIdentities.Select(device => device.DeviceFingerprintHash).Distinct().CountAsync());
    }

    private static DeviceIdentityService CreateService(
        AppDbContext dbContext,
        IHttpContextAccessor httpContextAccessor)
    {
        var securityOptions = Options.Create(new SubscriptionSecurityOptions
        {
            HmacSecret = "test-subscription-hmac-secret-with-32-chars",
            DeviceCookieName = "sachai_device",
            DeviceFingerprintHeaderName = "X-SachAI-Device-Fingerprint"
        });
        var hashService = new HmacPseudonymizationService(securityOptions);
        return new DeviceIdentityService(
            dbContext,
            httpContextAccessor,
            hashService,
            new TestClientIpResolver(),
            securityOptions,
            new TestHostEnvironment());
    }

    private static DefaultHttpContext CreateHttpContext(string fingerprint)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-SachAI-Device-Fingerprint"] = fingerprint;
        return context;
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private sealed class TestClientIpResolver : IClientIpResolver
    {
        public string? GetClientIpAddress()
        {
            return "203.0.113.10";
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "AiVideoDetection.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
