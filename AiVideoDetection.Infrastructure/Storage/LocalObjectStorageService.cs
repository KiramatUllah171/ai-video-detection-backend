using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Common;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Storage;

public class LocalObjectStorageService(
    IOptions<LocalStorageOptions> options,
    IApplicationEncryptionService encryptionService) : IObjectStorageService
{
    private readonly LocalStorageOptions _options = options.Value;

    public async Task<string> UploadAsync(
        Stream stream,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKey);
        var rootPath = Path.GetFullPath(_options.LocalRootPath);
        var destinationPath = Path.GetFullPath(Path.Combine(rootPath, safeObjectKey.Replace('/', Path.DirectorySeparatorChar)));

        if (!destinationPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage object key.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        await using var destination = File.Create(destinationPath);
        await encryptionService.ProtectStreamAsync(stream, destination, cancellationToken);

        return safeObjectKey;
    }

    public Task DeleteAsync(string objectKeyOrUrl, CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKeyOrUrl);
        var rootPath = Path.GetFullPath(_options.LocalRootPath);
        var path = Path.GetFullPath(Path.Combine(rootPath, safeObjectKey.Replace('/', Path.DirectorySeparatorChar)));

        if (path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) && File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public Task<string> GetReadUrlAsync(string objectKeyOrUrl, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(objectKeyOrUrl);
    }

    public async Task DownloadToAsync(
        string objectKeyOrUrl,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKeyOrUrl);
        var sourcePath = ResolveStoragePath(safeObjectKey);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Stored object was not found.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var tempDestinationPath = destinationPath + ".decrypting";
        if (File.Exists(tempDestinationPath))
        {
            File.Delete(tempDestinationPath);
        }

        try
        {
            await using (var source = File.OpenRead(sourcePath))
            await using (var destination = File.Create(tempDestinationPath))
            {
                await encryptionService.UnprotectStreamAsync(source, destination, cancellationToken);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempDestinationPath, destinationPath);
        }
        catch
        {
            if (File.Exists(tempDestinationPath))
            {
                File.Delete(tempDestinationPath);
            }

            throw;
        }
    }

    private static string NormalizeObjectKey(string objectKey)
    {
        var normalized = objectKey.Replace('\\', '/').TrimStart('/');
        if (normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage object key.");
        }

        return normalized;
    }

    private string ResolveStoragePath(string safeObjectKey)
    {
        var rootPath = Path.GetFullPath(_options.LocalRootPath);
        var path = Path.GetFullPath(Path.Combine(rootPath, safeObjectKey.Replace('/', Path.DirectorySeparatorChar)));

        if (!path.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Invalid storage object key.");
        }

        return path;
    }
}
