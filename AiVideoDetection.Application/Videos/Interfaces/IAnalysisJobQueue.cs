namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IAnalysisJobQueue
{
    string EnqueueAnalysisJob(long jobId);

    string RetryAnalysisJob(long jobId);

    string ResumeAnalysisJob(long jobId);
}
