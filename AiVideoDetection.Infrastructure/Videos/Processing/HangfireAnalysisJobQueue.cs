using AiVideoDetection.Application.Videos.Interfaces;
using Hangfire;

namespace AiVideoDetection.Infrastructure.Videos.Processing;

public class HangfireAnalysisJobQueue(IBackgroundJobClient backgroundJobClient) : IAnalysisJobQueue
{
    public string EnqueueAnalysisJob(long jobId)
    {
        return backgroundJobClient.Enqueue<AnalysisJobProcessor>(
            "analysis",
            processor => processor.ProcessAsync(jobId, CancellationToken.None));
    }

    public string RetryAnalysisJob(long jobId)
    {
        return EnqueueAnalysisJob(jobId);
    }

    public string ResumeAnalysisJob(long jobId)
    {
        return EnqueueAnalysisJob(jobId);
    }
}
