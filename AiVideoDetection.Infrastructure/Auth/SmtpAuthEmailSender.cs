using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Domain.Entities;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MailKitSmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace AiVideoDetection.Infrastructure.Auth;

public class SmtpAuthEmailSender : IPasswordResetEmailSender, IEmailConfirmationSender
{
    private readonly PasswordResetOptions _options;
    private readonly ILogger<SmtpAuthEmailSender> _logger;
    private readonly Func<IAuthSmtpClient> _smtpClientFactory;

    public SmtpAuthEmailSender(
        IOptions<PasswordResetOptions> options,
        ILogger<SmtpAuthEmailSender> logger)
        : this(options, logger, static () => new MailKitAuthSmtpClient())
    {
    }

    internal SmtpAuthEmailSender(
        IOptions<PasswordResetOptions> options,
        ILogger<SmtpAuthEmailSender> logger,
        Func<IAuthSmtpClient> smtpClientFactory)
    {
        _options = options.Value;
        _logger = logger;
        _smtpClientFactory = smtpClientFactory;
    }

    public Task SendPasswordResetAsync(User user, string resetUrl, CancellationToken cancellationToken = default)
    {
        return SendSmtpAsync(
            user,
            "Reset your SachAI password",
            $"""
Hello {user.Name},

We received a request to reset the password for your SachAI account.

Use the secure link below to choose a new password:

{resetUrl}

For your security, this link will expire automatically. If you did not request a password reset, you can safely ignore this email and your password will remain unchanged.

Regards,
SachAI Team
""",
            "Password reset email sent for user id {UserId}.",
            cancellationToken);
    }

    public Task SendEmailConfirmationAsync(User user, string confirmationUrl, CancellationToken cancellationToken = default)
    {
        return SendSmtpAsync(
            user,
            "Confirm your SachAI email address",
            $"""
Hello {user.Name},

Thank you for creating a SachAI account.

Please confirm your email address using the secure link below:

{confirmationUrl}

For your security, this link will expire automatically. If you did not create this account, you can safely ignore this email.

Regards,
SachAI Team
""",
            "Email confirmation email sent for user id {UserId}.",
            cancellationToken);
    }

    private async Task SendSmtpAsync(
        User user,
        string subject,
        string body,
        string successLogMessage,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(_options.Provider, "Smtp", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Auth email provider must be configured as Smtp.");
        }

        var host = string.IsNullOrWhiteSpace(_options.Host) ? _options.SmtpHost : _options.Host;
        var port = _options.Port > 0 ? _options.Port : _options.SmtpPort;
        var username = string.IsNullOrWhiteSpace(_options.Username) ? _options.SmtpUsername : _options.Username;
        var password = string.IsNullOrWhiteSpace(_options.Password) ? _options.SmtpPassword : _options.Password;
        var senderEmail = string.IsNullOrWhiteSpace(_options.SenderEmail) ? _options.FromEmail : _options.SenderEmail;
        var senderName = string.IsNullOrWhiteSpace(_options.SenderName) ? _options.FromName : _options.SenderName;
        var recipientEmail = user.Email;

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(senderEmail)
            || string.IsNullOrWhiteSpace(recipientEmail))
        {
            _logger.LogError("SMTP email delivery is not configured. Provider={Provider}; HostConfigured={HostConfigured}; UsernameConfigured={UsernameConfigured}; SenderConfigured={SenderConfigured}; PasswordConfigured={PasswordConfigured}.",
                _options.Provider,
                !string.IsNullOrWhiteSpace(host),
                !string.IsNullOrWhiteSpace(username),
                !string.IsNullOrWhiteSpace(senderEmail),
                !string.IsNullOrWhiteSpace(password));
            throw new InvalidOperationException("SMTP email delivery is not configured.");
        }

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(senderName, senderEmail));
        message.To.Add(MailboxAddress.Parse(recipientEmail));
        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        using var client = _smtpClientFactory();
        await client.ConnectAsync(host, port, SecureSocketOptions.StartTls, cancellationToken);
        await client.AuthenticateAsync(username, password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        _logger.LogInformation(successLogMessage, user.Id);
    }
}

internal interface IAuthSmtpClient : IDisposable
{
    Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken);

    Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);

    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);

    Task DisconnectAsync(bool quit, CancellationToken cancellationToken);
}

internal sealed class MailKitAuthSmtpClient : IAuthSmtpClient
{
    private readonly MailKitSmtpClient _client = new();

    public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken)
    {
        return _client.ConnectAsync(host, port, options, cancellationToken);
    }

    public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
    {
        return _client.AuthenticateAsync(userName, password, cancellationToken);
    }

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        return _client.SendAsync(message, cancellationToken);
    }

    public Task DisconnectAsync(bool quit, CancellationToken cancellationToken)
    {
        return _client.DisconnectAsync(quit, cancellationToken);
    }

    public void Dispose()
    {
        _client.Dispose();
    }
}
