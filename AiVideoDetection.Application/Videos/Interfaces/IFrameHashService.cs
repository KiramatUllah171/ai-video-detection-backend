using AiVideoDetection.Application.Videos.Matching;
using AiVideoDetection.Domain.Entities;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IFrameHashService
{
    Task<IReadOnlyList<FrameHashResult>> GenerateHashesForVideoAsync(
        long videoId,
        CancellationToken cancellationToken = default);

    Task<FrameHashResult> GenerateHashForFrameAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default);
}
