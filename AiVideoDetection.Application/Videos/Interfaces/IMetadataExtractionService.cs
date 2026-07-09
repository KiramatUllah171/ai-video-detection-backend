using AiVideoDetection.Application.Videos.Processing;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IMetadataExtractionService
{
    Task<ExtractedMetadataResult> ExtractMetadataAsync(VideoProcessingInput input, CancellationToken cancellationToken);
}
