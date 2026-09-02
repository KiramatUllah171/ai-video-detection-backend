using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Infrastructure.Common;

public sealed class NoOpApplicationEncryptionService : IApplicationEncryptionService
{
    public static NoOpApplicationEncryptionService Instance { get; } = new();

    public bool EncryptStorageObjects => false;

    public bool EncryptDatabaseFields => false;

    private NoOpApplicationEncryptionService()
    {
    }

    public string? ProtectString(string? plaintext)
    {
        return plaintext;
    }

    public string? UnprotectString(string? protectedValue)
    {
        return protectedValue;
    }

    public Task ProtectStreamAsync(Stream plaintext, Stream destination, CancellationToken cancellationToken = default)
    {
        return plaintext.CopyToAsync(destination, cancellationToken);
    }

    public Task UnprotectStreamAsync(Stream protectedStream, Stream destination, CancellationToken cancellationToken = default)
    {
        return protectedStream.CopyToAsync(destination, cancellationToken);
    }
}
