using AiVideoDetection.Application.Videos;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Storage;
using AiVideoDetection.Infrastructure.Videos.StorageProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class DiskVideoStorageCapacityServiceTests
{
    [Fact]
    public async Task CheckUploadCapacityFailsCleanlyWhenRequiredReserveCannotBeMet()
    {
        var root = CreateTempRoot();
        var service = CreateService(root, new VideoStorageProtectionOptions
        {
            MinimumFreeSpaceReserveBytes = long.MaxValue,
            UploadTempRootPath = Path.Combine(root, "upload-temp")
        });

        var result = await service.CheckUploadCapacityAsync(1024);

        Assert.False(result.HasCapacity);
        Assert.Equal(VideoInfrastructureErrorCodes.ServerStorageCapacityLow, result.ErrorCode);
        Assert.DoesNotContain(root, result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckProcessingCapacityCanBeDisabledForNonDiskBackedTestRuns()
    {
        var root = CreateTempRoot();
        var service = CreateService(root, new VideoStorageProtectionOptions
        {
            EnableFreeDiskChecks = false,
            MinimumFreeSpaceReserveBytes = long.MaxValue,
            UploadTempRootPath = Path.Combine(root, "upload-temp")
        });

        var result = await service.CheckProcessingCapacityAsync(long.MaxValue);

        Assert.True(result.HasCapacity);
    }

    private static DiskVideoStorageCapacityService CreateService(
        string root,
        VideoStorageProtectionOptions storageProtectionOptions)
    {
        return new DiskVideoStorageCapacityService(
            Options.Create(storageProtectionOptions),
            Options.Create(new VideoProcessingOptions
            {
                WorkingRootPath = Path.Combine(root, "work")
            }),
            Options.Create(new LocalStorageOptions
            {
                Provider = "Local",
                LocalRootPath = Path.Combine(root, "private")
            }),
            NullLogger<DiskVideoStorageCapacityService>.Instance);
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "ai-video-capacity-tests", Guid.NewGuid().ToString("N"));
    }
}
