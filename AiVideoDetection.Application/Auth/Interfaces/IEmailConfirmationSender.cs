using AiVideoDetection.Domain.Entities;

namespace AiVideoDetection.Application.Auth.Interfaces;

public interface IEmailConfirmationSender
{
    Task SendEmailConfirmationAsync(User user, string confirmationUrl, CancellationToken cancellationToken = default);
}
