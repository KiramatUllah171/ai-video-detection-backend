namespace AiVideoDetection.Application.Common;

public interface IApplicationEncryptionService
{
    bool EncryptStorageObjects { get; }

    bool EncryptDatabaseFields { get; }

    string? ProtectString(string? plaintext);

    string? UnprotectString(string? protectedValue);

    Task ProtectStreamAsync(Stream plaintext, Stream destination, CancellationToken cancellationToken = default);

    Task UnprotectStreamAsync(Stream protectedStream, Stream destination, CancellationToken cancellationToken = default);
}
