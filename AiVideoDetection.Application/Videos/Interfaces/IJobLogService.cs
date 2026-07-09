namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IJobLogService
{
    Task LogAsync(
        long jobId,
        string stepName,
        string level,
        string message,
        object? details = null,
        CancellationToken cancellationToken = default);
}
