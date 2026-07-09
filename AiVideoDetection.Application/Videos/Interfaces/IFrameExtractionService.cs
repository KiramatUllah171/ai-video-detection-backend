using AiVideoDetection.Application.Videos.Processing;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IFrameExtractionService
{
    Task<ThumbnailResult> GenerateThumbnailAsync(VideoProcessingInput input, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExtractedFrameResult>> ExtractFramesAsync(VideoProcessingInput input, CancellationToken cancellationToken);
}
