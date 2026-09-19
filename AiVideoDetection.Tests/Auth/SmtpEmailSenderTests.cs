using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Auth;

public class SmtpEmailSenderTests
{
    [Fact]
    public async Task SendPasswordResetAsync_RequiresSmtpPassword()
    {
        var sender = new SmtpAuthEmailSender(
            Options.Create(new PasswordResetOptions
            {
                Provider = "Smtp",
                Host = "smtp.email.me-dubai-1.oci.oraclecloud.com",
                Port = 587,
                UseStartTls = true,
                UseSsl = false,
                Username = "ocid1.user.oc1..smtp-credential",
                Password = "",
                SenderEmail = "noreply@sachaitech.com",
                SenderName = "SachAI"
            }),
            NullLogger<SmtpAuthEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendPasswordResetAsync(TestUser(), "https://sachaitech.com/reset-password?token=test"));
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_RequiresSmtpProvider()
    {
        var sender = new SmtpAuthEmailSender(
            Options.Create(new PasswordResetOptions
            {
                Provider = "DevelopmentLog",
                Host = "smtp.email.me-dubai-1.oci.oraclecloud.com",
                Port = 587,
                UseStartTls = true,
                Username = "ocid1.user.oc1..smtp-credential",
                Password = "not-used",
                SenderEmail = "noreply@sachaitech.com",
                SenderName = "SachAI"
            }),
            NullLogger<SmtpAuthEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendEmailConfirmationAsync(TestUser(), "https://sachaitech.com/confirm-email?token=test"));
    }

    private static User TestUser()
    {
        return new User
        {
            Id = 1,
            Name = "Test User",
            Email = "recipient@example.com",
            IsActive = true
        };
    }
}
