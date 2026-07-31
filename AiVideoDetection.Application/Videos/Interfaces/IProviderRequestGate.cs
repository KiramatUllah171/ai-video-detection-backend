namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IProviderRequestGate
{
    Task<IAsyncDisposable> EnterAsync(
        long videoId,
        long jobId,
        int? segmentIndex,
        int? segmentAttempt,
        string providerMode,
        CancellationToken cancellationToken = default);
}
