using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Interfaces;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Storage;

public sealed class R2ObjectStorageService(
    IAmazonS3 s3Client,
    IOptions<R2Options> options,
    IApplicationEncryptionService encryptionService) : IObjectStorageService
{
    private readonly R2Options _options = options.Value;

    public async Task<string> UploadAsync(
        Stream stream,
        string objectKey,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKey);
        var tempPath = Path.Combine(Path.GetTempPath(), $"r2-upload-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var encrypted = File.Create(tempPath))
            {
                await encryptionService.ProtectStreamAsync(stream, encrypted, cancellationToken);
            }

            await using var upload = File.OpenRead(tempPath);
            await s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = safeObjectKey,
                InputStream = upload,
                ContentType = contentType,
                AutoCloseStream = false
            }, cancellationToken);

            return safeObjectKey;
        }
        finally
        {
            DeleteTempFile(tempPath);
        }
    }

    public async Task DeleteAsync(string objectKeyOrUrl, CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKeyOrUrl);
        await s3Client.DeleteObjectAsync(_options.BucketName, safeObjectKey, cancellationToken);
    }

    public Task<string> GetReadUrlAsync(string objectKeyOrUrl, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKeyOrUrl);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = safeObjectKey,
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(expiry)
        };

        return Task.FromResult(s3Client.GetPreSignedURL(request));
    }

    public async Task DownloadToAsync(
        string objectKeyOrUrl,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var safeObjectKey = NormalizeObjectKey(objectKeyOrUrl);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        var tempDestinationPath = destinationPath + ".decrypting";
        DeleteTempFile(tempDestinationPath);

        try
        {
            using var response = await s3Client.GetObjectAsync(_options.BucketName, safeObjectKey, cancellationToken);
            await using (var destination = File.Create(tempDestinationPath))
            {
                await encryptionService.UnprotectStreamAsync(response.ResponseStream, destination, cancellationToken);
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(tempDestinationPath, destinationPath);
        }
        catch
        {
            DeleteTempFile(tempDestinationPath);
            throw;
        }
    }

    private string NormalizeObjectKey(string objectKeyOrUrl)
    {
        var value = objectKeyOrUrl.Replace('\\', '/').Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            value = uri.AbsolutePath;
        }

        var normalized = Uri.UnescapeDataString(value).TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage object key.");
        }

        return normalized;
    }

    private static void DeleteTempFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
