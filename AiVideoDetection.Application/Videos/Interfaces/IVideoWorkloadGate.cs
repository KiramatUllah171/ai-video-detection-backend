namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IVideoWorkloadGate
{
    Task<IAsyncDisposable?> TryEnterUploadAsync(CancellationToken cancellationToken = default);

    Task<IAsyncDisposable> EnterProcessingAsync(CancellationToken cancellationToken = default);
}
