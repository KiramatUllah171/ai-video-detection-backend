using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using IdentityPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<AiVideoDetection.Domain.Entities.User>;

namespace AiVideoDetection.Infrastructure.Auth;

public class PasswordHasher : IPasswordHasher
{
    private readonly IdentityPasswordHasher _identityPasswordHasher = new();

    public string HashPassword(string password)
    {
        return _identityPasswordHasher.HashPassword(new User(), password);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            return false;
        }

        if (passwordHash.StartsWith("AQAAAA", StringComparison.Ordinal))
        {
            var result = _identityPasswordHasher.VerifyHashedPassword(new User(), passwordHash, password);
            return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}
