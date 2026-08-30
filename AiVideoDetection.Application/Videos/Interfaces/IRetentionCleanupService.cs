namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IRetentionCleanupService
{
    Task CleanupTemporaryFilesAsync();

    Task CleanupExpiredRetainedAssetsAsync();
}
