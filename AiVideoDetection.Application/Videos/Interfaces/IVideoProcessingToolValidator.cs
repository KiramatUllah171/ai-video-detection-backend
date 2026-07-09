using AiVideoDetection.Application.Videos.Processing;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IVideoProcessingToolValidator
{
    Task<VideoProcessingToolCheckResult> ValidateAsync(CancellationToken cancellationToken = default);
}
