using AiVideoDetection.Application.Videos.Interfaces;
using Hangfire;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class AnalysisJobProcessor(IVideoProcessingService videoProcessingService)
{
    [Queue("analysis")]
    [AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task ProcessAsync(long jobId, CancellationToken cancellationToken = default)
    {
        return videoProcessingService.ProcessAnalysisJobAsync(jobId, cancellationToken);
    }
}
