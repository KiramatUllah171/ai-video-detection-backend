using AiVideoDetection.Application.Videos;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.StorageProtection;

public sealed class DiskVideoStorageCapacityService(
    IOptions<VideoStorageProtectionOptions> storageProtectionOptions,
    IOptions<VideoProcessingOptions> processingOptions,
    IOptions<LocalStorageOptions> localStorageOptions,
    ILogger<DiskVideoStorageCapacityService> logger) : IVideoStorageCapacityService
{
    private const string CapacityMessage = "The server is temporarily unable to accept this video. Please try again later.";
    private readonly VideoStorageProtectionOptions _storageOptions = storageProtectionOptions.Value;
    private readonly VideoProcessingOptions _processingOptions = processingOptions.Value;
    private readonly LocalStorageOptions _localStorageOptions = localStorageOptions.Value;

    public Task<VideoStorageCapacityCheckResult> CheckUploadCapacityAsync(
        long originalFileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        var requirements = new List<CapacityRequirement>
        {
            new(Path.GetTempPath(), Estimate(originalFileSizeBytes, _storageOptions.MultipartTempSpaceMultiplier)),
            new(_storageOptions.UploadTempRootPath, Estimate(originalFileSizeBytes, _storageOptions.UploadTempSpaceMultiplier))
        };

        if (string.Equals(_localStorageOptions.Provider, "Local", StringComparison.OrdinalIgnoreCase))
        {
            requirements.Add(new(
                _localStorageOptions.LocalRootPath,
                Estimate(originalFileSizeBytes, _storageOptions.LocalStorageSpaceMultiplier)));
        }

        return Task.FromResult(CheckCapacity(requirements));
    }

    public Task<VideoStorageCapacityCheckResult> CheckProcessingCapacityAsync(
        long originalFileSizeBytes,
        CancellationToken cancellationToken = default)
    {
        var requirements = new List<CapacityRequirement>
        {
            new(_processingOptions.WorkingRootPath, Estimate(originalFileSizeBytes, _storageOptions.ProcessingWorkingSpaceMultiplier))
        };

        return Task.FromResult(CheckCapacity(requirements));
    }

    private VideoStorageCapacityCheckResult CheckCapacity(IReadOnlyList<CapacityRequirement> requirements)
    {
        if (!_storageOptions.EnableFreeDiskChecks)
        {
            return VideoStorageCapacityCheckResult.Succeeded();
        }

        try
        {
            var requiredByRoot = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var requirement in requirements)
            {
                var path = Path.GetFullPath(requirement.Path);
                Directory.CreateDirectory(path);
                var root = Path.GetPathRoot(path);
                if (string.IsNullOrWhiteSpace(root))
                {
                    logger.LogWarning("Unable to resolve disk root for storage-capacity path.");
                    return VideoStorageCapacityCheckResult.Failed(
                        VideoInfrastructureErrorCodes.ServerStorageCapacityLow,
                        CapacityMessage);
                }

                requiredByRoot[root] = AddClamped(requiredByRoot.GetValueOrDefault(root), requirement.RequiredBytes);
            }

            foreach (var (root, requiredBytes) in requiredByRoot)
            {
                var drive = new DriveInfo(root);
                var requiredWithReserve = AddClamped(requiredBytes, _storageOptions.MinimumFreeSpaceReserveBytes);
                if (drive.AvailableFreeSpace < requiredWithReserve)
                {
                    logger.LogWarning(
                        "Video storage capacity check failed for root {Root}. Required {RequiredBytes} bytes including reserve; available {AvailableBytes} bytes.",
                        root,
                        requiredWithReserve,
                        drive.AvailableFreeSpace);
                    return VideoStorageCapacityCheckResult.Failed(
                        VideoInfrastructureErrorCodes.ServerStorageCapacityLow,
                        CapacityMessage);
                }
            }

            return VideoStorageCapacityCheckResult.Succeeded();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(exception, "Video storage capacity check failed before disk values could be confirmed.");
            return VideoStorageCapacityCheckResult.Failed(
                VideoInfrastructureErrorCodes.ServerStorageCapacityLow,
                CapacityMessage);
        }
    }

    private static long Estimate(long originalFileSizeBytes, decimal multiplier)
    {
        var estimate = Math.Ceiling(Math.Max(0, originalFileSizeBytes) * Math.Max(0, multiplier));
        return estimate >= long.MaxValue ? long.MaxValue : (long)estimate;
    }

    private static long AddClamped(long left, long right)
    {
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    private sealed record CapacityRequirement(string Path, long RequiredBytes);
}
