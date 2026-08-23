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
                Host = "smtp.gmail.com",
                Port = 587,
                UseStartTls = true,
                UseSsl = false,
                Username = "kiramatullahcomputer@gmail.com",
                Password = "",
                SenderEmail = "kiramatullahcomputer@gmail.com",
                SenderName = "sachvideoai"
            }),
            NullLogger<SmtpAuthEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendPasswordResetAsync(TestUser(), "http://localhost:5173/reset-password?token=test"));
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_RequiresSmtpProvider()
    {
        var sender = new SmtpAuthEmailSender(
            Options.Create(new PasswordResetOptions
            {
                Provider = "DevelopmentLog",
                Host = "smtp.gmail.com",
                Port = 587,
                UseStartTls = true,
                Username = "kiramatullahcomputer@gmail.com",
                Password = "not-used",
                SenderEmail = "kiramatullahcomputer@gmail.com",
                SenderName = "sachvideoai"
            }),
            NullLogger<SmtpAuthEmailSender>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendEmailConfirmationAsync(TestUser(), "http://localhost:5173/confirm-email?token=test"));
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
