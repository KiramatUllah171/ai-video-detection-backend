namespace AiVideoDetection.Application.Common;

public sealed class ApplicationEncryptionOptions
{
    public const string SectionName = "Encryption";

    public bool Enabled { get; set; } = true;

    public bool EncryptStorageObjects { get; set; } = true;

    public bool EncryptDatabaseFields { get; set; } = true;

    public bool AllowPlaintextFallback { get; set; } = true;

    public string? MasterKeyBase64 { get; set; }
}
