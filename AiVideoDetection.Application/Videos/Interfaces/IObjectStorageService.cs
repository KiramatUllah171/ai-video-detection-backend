namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IObjectStorageService
{
    Task<string> UploadAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKeyOrUrl, CancellationToken cancellationToken = default);

    Task<string> GetReadUrlAsync(string objectKeyOrUrl, TimeSpan expiry, CancellationToken cancellationToken = default);
}
