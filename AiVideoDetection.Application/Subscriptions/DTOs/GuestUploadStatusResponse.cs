namespace AiVideoDetection.Application.Subscriptions.DTOs;

public class GuestUploadStatusResponse
{
    public bool CanUpload { get; init; }
    public int RemainingUploads { get; init; }
    public string? BlockReasonCode { get; init; }
    public long? MaxVideoSizeBytes { get; init; }
}
