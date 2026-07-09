namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IVideoProcessingService
{
    Task ProcessAnalysisJobAsync(long jobId, CancellationToken cancellationToken = default);
}
