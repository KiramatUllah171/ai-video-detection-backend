using AiVideoDetection.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace AiVideoDetection.Domain.Entities;

public class User : IdentityUser<long>
{
    public User()
    {
        SecurityStamp = Guid.NewGuid().ToString("N");
        ConcurrencyStamp = Guid.NewGuid().ToString("N");
        LockoutEnabled = true;
    }

    public string Name { get; set; } = string.Empty;

    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = [];

    public ICollection<EmailConfirmationToken> EmailConfirmationTokens { get; set; } = [];

    public ICollection<Video> Videos { get; set; } = [];
}
