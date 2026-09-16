using System.Security.Cryptography;

namespace AiVideoDetection.Infrastructure.Videos;

internal static class GuestVideoAccessToken
{
    public static string Generate()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool IsValid(string token)
    {
        return !string.IsNullOrWhiteSpace(token) && token.Trim().Length is >= 32 and <= 256;
    }
}
