using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Auth;
using Microsoft.Extensions.Configuration;

namespace AiVideoDetection.Tests.Api;

public sealed class ProductionConfigurationTests
{
    [Fact]
    public void ProductionConfigurationSelectsHybridBitMindProvider()
    {
        var configuration = LoadApiConfiguration();
        var options = configuration.GetSection(AiServiceOptions.SectionName).Get<AiServiceOptions>()
            ?? throw new InvalidOperationException("AiService configuration was not found.");

        Assert.Equal("http://ai-service:8000", options.BaseUrl);
        Assert.Equal("hybrid", options.ProviderMode);
        Assert.True(options.BitMindEnabled);
        Assert.Equal("OnUncertain", options.ExternalProviderPolicy);
        Assert.True(options.LocalFallbackEnabled);
    }

    [Fact]
    public void ProductionConfigurationUsesPublicFrontendForEmailLinks()
    {
        var configuration = LoadApiConfiguration();
        var options = configuration.GetSection(PasswordResetOptions.SectionName).Get<PasswordResetOptions>()
            ?? throw new InvalidOperationException("PasswordReset configuration was not found.");

        Assert.Equal("https://sachaitech.com", options.FrontendBaseUrl);
    }

    [Fact]
    public void ProductionConfigurationUsesOciEmailDelivery()
    {
        var configuration = LoadApiConfiguration();
        var options = configuration.GetSection(PasswordResetOptions.SectionName).Get<PasswordResetOptions>()
            ?? throw new InvalidOperationException("PasswordReset configuration was not found.");

        Assert.Equal("Smtp", options.Provider);
        Assert.Equal("smtp.email.me-dubai-1.oci.oraclecloud.com", options.Host);
        Assert.Equal(587, options.Port);
        Assert.True(options.UseStartTls);
        Assert.False(options.UseSsl);
        Assert.Equal(string.Empty, options.Username);
        Assert.Equal(string.Empty, options.Password);
        Assert.Equal("noreply@sachaitech.com", options.SenderEmail);
        Assert.Equal("SachAI", options.SenderName);
        Assert.Equal("smtp.email.me-dubai-1.oci.oraclecloud.com", options.SmtpHost);
        Assert.Equal(587, options.SmtpPort);
        Assert.True(options.SmtpEnableSsl);
        Assert.Equal(string.Empty, options.SmtpUsername);
        Assert.Equal(string.Empty, options.SmtpPassword);
        Assert.Equal("noreply@sachaitech.com", options.FromEmail);
        Assert.Equal("SachAI", options.FromName);
    }

    private static IConfigurationRoot LoadApiConfiguration()
    {
        var apiDirectory = FindApiDirectory();
        return new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();
    }

    private static string FindApiDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "AiVideoDetection.Api");
            if (Directory.Exists(candidate)
                && File.Exists(Path.Combine(candidate, "appsettings.Production.json")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate AiVideoDetection.Api.");
    }
}
