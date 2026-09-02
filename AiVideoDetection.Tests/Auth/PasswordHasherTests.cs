using AiVideoDetection.Infrastructure.Auth;

namespace AiVideoDetection.Tests.Auth;

public class PasswordHasherTests
{
    [Fact]
    public void VerifyPassword_ReturnsTrue_ForCorrectPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.HashPassword("Password123");

        Assert.True(hasher.VerifyPassword("Password123", hash));
    }

    [Fact]
    public void VerifyPassword_ReturnsFalse_ForWrongPassword()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.HashPassword("Password123");

        Assert.False(hasher.VerifyPassword("WrongPassword123", hash));
    }

    [Fact]
    public void VerifyPassword_ReturnsTrue_ForLegacyBcryptHash()
    {
        var hasher = new PasswordHasher();
        var legacyHash = BCrypt.Net.BCrypt.HashPassword("Password123");

        Assert.True(hasher.VerifyPassword("Password123", legacyHash));
    }
}
