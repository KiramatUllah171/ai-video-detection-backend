namespace AiVideoDetection.Application.Videos.Options;

public class VideoStorageProtectionOptions
{
    public const string SectionName = "VideoStorageProtection";

    public bool EnableFreeDiskChecks { get; set; } = true;

    public string UploadTempRootPath { get; set; } = "storage/upload-temp";

    public decimal MultipartTempSpaceMultiplier { get; set; } = 1.05m;

    public decimal UploadTempSpaceMultiplier { get; set; } = 1.10m;

    public decimal LocalStorageSpaceMultiplier { get; set; } = 1.10m;

    public decimal ProcessingWorkingSpaceMultiplier { get; set; } = 4.00m;

    public long MinimumFreeSpaceReserveBytes { get; set; } = 1_073_741_824;

    public int MaxConcurrentUploads { get; set; } = 3;

    public int MaxConcurrentProcessingJobs { get; set; } = 2;

    public int UploadConcurrencyWaitTimeoutSeconds { get; set; } = 5;

    public int OrphanUploadTempRetentionHours { get; set; } = 4;

    public TimeSpan UploadConcurrencyWaitTimeout => TimeSpan.FromSeconds(UploadConcurrencyWaitTimeoutSeconds);

    public TimeSpan OrphanUploadTempRetention => TimeSpan.FromHours(OrphanUploadTempRetentionHours);
}
