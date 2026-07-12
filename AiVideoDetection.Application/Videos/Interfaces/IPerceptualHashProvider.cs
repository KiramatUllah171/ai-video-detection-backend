using AiVideoDetection.Application.Videos.Matching;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IPerceptualHashProvider
{
    Task<PerceptualHashResult> GenerateAsync(
        FrameHashInput input,
        CancellationToken cancellationToken = default);
}
