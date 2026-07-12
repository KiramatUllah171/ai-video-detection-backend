using System.Security.Cryptography;
using System.Text;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Matching;

namespace AiVideoDetection.Infrastructure.Videos.Matching;

public class PlaceholderPerceptualHashProvider : IPerceptualHashProvider
{
    public Task<PerceptualHashResult> GenerateAsync(
        FrameHashInput input,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // MVP placeholder hash. Replace with real perceptual image hashing later.
        var stableSource = string.IsNullOrWhiteSpace(input.SourceVideoHash)
            ? input.FrameUrl
            : input.SourceVideoHash;
        var seed = $"{stableSource}|{input.FrameIndex}|{input.HashVersion}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();

        return Task.FromResult(new PerceptualHashResult(
            hex[..16],
            hex[16..32],
            hex[32..48]));
    }
}
