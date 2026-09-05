namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IVideoStorageCapacityService
{
    Task<VideoStorageCapacityCheckResult> CheckUploadCapacityAsync(
        long originalFileSizeBytes,
        CancellationToken cancellationToken = default);

    Task<VideoStorageCapacityCheckResult> CheckProcessingCapacityAsync(
        long originalFileSizeBytes,
        CancellationToken cancellationToken = default);
}

public sealed record VideoStorageCapacityCheckResult(
    bool HasCapacity,
    string? ErrorCode = null,
    string? Message = null)
{
    public static VideoStorageCapacityCheckResult Succeeded() => new(true);

    public static VideoStorageCapacityCheckResult Failed(string errorCode, string message) => new(false, errorCode, message);
}
