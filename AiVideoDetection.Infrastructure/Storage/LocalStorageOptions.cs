namespace AiVideoDetection.Infrastructure.Storage;

public class LocalStorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; set; } = "Local";

    public string LocalRootPath { get; set; } = "storage/private";
}
