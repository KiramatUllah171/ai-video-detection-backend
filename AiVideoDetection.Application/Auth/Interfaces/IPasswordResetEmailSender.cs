using AiVideoDetection.Domain.Entities;

namespace AiVideoDetection.Application.Auth.Interfaces;

public interface IPasswordResetEmailSender
{
    Task SendPasswordResetAsync(User user, string resetUrl, CancellationToken cancellationToken = default);
}
