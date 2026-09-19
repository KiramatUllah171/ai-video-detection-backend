using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Auth;
using MailKit.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MimeKit;

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

    [Fact]
    public async Task SendEmailConfirmationAsync_UsesMailKitStartTlsAndConfiguredOciCredentials()
    {
        var smtpClient = new FakeAuthSmtpClient();
        var sender = new SmtpAuthEmailSender(
            Options.Create(new PasswordResetOptions
            {
                Provider = "Smtp",
                Host = "smtp.email.me-dubai-1.oci.oraclecloud.com",
                Port = 587,
                UseStartTls = true,
                UseSsl = false,
                Username = "oci-smtp-user",
                Password = "oci-smtp-password",
                SenderEmail = "noreply@sachaitech.com",
                SenderName = "SachAI"
            }),
            NullLogger<SmtpAuthEmailSender>.Instance,
            () => smtpClient);

        await sender.SendEmailConfirmationAsync(TestUser(), "https://sachaitech.com/confirm-email?token=test");

        Assert.Equal("smtp.email.me-dubai-1.oci.oraclecloud.com", smtpClient.Host);
        Assert.Equal(587, smtpClient.Port);
        Assert.Equal(SecureSocketOptions.StartTls, smtpClient.SecureSocketOptions);
        Assert.Equal("oci-smtp-user", smtpClient.AuthenticatedUserName);
        Assert.Equal("oci-smtp-password", smtpClient.AuthenticatedPassword);
        Assert.True(smtpClient.WasDisconnectedWithQuit);
        Assert.Equal("Confirm your SachAI email address", smtpClient.Subject);
        Assert.Equal("SachAI", smtpClient.FromName);
        Assert.Equal("noreply@sachaitech.com", smtpClient.FromAddress);
        Assert.Equal("recipient@example.com", smtpClient.ToAddress);
        Assert.Contains("https://sachaitech.com/confirm-email?token=test", smtpClient.BodyText);
    }

    [Fact]
    public async Task SendPasswordResetAsync_UsesConfiguredSenderAndPreservesResetBody()
    {
        var smtpClient = new FakeAuthSmtpClient();
        var sender = new SmtpAuthEmailSender(
            Options.Create(new PasswordResetOptions
            {
                Provider = "Smtp",
                Host = "smtp.email.me-dubai-1.oci.oraclecloud.com",
                Port = 587,
                Username = "oci-smtp-user",
                Password = "oci-smtp-password",
                SenderEmail = "noreply@sachaitech.com",
                SenderName = "SachAI"
            }),
            NullLogger<SmtpAuthEmailSender>.Instance,
            () => smtpClient);

        await sender.SendPasswordResetAsync(TestUser(), "https://sachaitech.com/reset-password?token=test");

        Assert.Equal("Reset your SachAI password", smtpClient.Subject);
        Assert.Equal("noreply@sachaitech.com", smtpClient.FromAddress);
        Assert.Contains("https://sachaitech.com/reset-password?token=test", smtpClient.BodyText);
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

    private sealed class FakeAuthSmtpClient : IAuthSmtpClient
    {
        public string? Host { get; private set; }
        public int Port { get; private set; }
        public SecureSocketOptions SecureSocketOptions { get; private set; }
        public string? AuthenticatedUserName { get; private set; }
        public string? AuthenticatedPassword { get; private set; }
        public string? Subject { get; private set; }
        public string? FromName { get; private set; }
        public string? FromAddress { get; private set; }
        public string? ToAddress { get; private set; }
        public string? BodyText { get; private set; }
        public bool WasDisconnectedWithQuit { get; private set; }

        public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken)
        {
            Host = host;
            Port = port;
            SecureSocketOptions = options;
            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            AuthenticatedUserName = userName;
            AuthenticatedPassword = password;
            return Task.CompletedTask;
        }

        public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            Subject = message.Subject;
            var from = message.From.Mailboxes.Single();
            FromName = from.Name;
            FromAddress = from.Address;
            ToAddress = message.To.Mailboxes.Single().Address;
            BodyText = Assert.IsType<TextPart>(message.Body).Text;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
        {
            WasDisconnectedWithQuit = quit;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
