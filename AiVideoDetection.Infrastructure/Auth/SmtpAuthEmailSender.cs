using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Mail;

namespace AiVideoDetection.Infrastructure.Auth;

public class SmtpAuthEmailSender(
    IOptions<PasswordResetOptions> options,
    ILogger<SmtpAuthEmailSender> logger) : IPasswordResetEmailSender, IEmailConfirmationSender
{
    private readonly PasswordResetOptions _options = options.Value;

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

        if (string.IsNullOrWhiteSpace(host)
            || string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
            || string.IsNullOrWhiteSpace(senderEmail))
        {
            logger.LogError("SMTP email delivery is not configured. Provider={Provider}; HostConfigured={HostConfigured}; UsernameConfigured={UsernameConfigured}; SenderConfigured={SenderConfigured}; PasswordConfigured={PasswordConfigured}.",
                _options.Provider,
                !string.IsNullOrWhiteSpace(host),
                !string.IsNullOrWhiteSpace(username),
                !string.IsNullOrWhiteSpace(senderEmail),
                !string.IsNullOrWhiteSpace(password));
            throw new InvalidOperationException("SMTP email delivery is not configured.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(senderEmail, senderName),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(user.Email);

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = _options.UseStartTls || _options.UseSsl || _options.SmtpEnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password)
        };

        await client.SendMailAsync(message, cancellationToken);
        logger.LogInformation(successLogMessage, user.Id);
    }
}
