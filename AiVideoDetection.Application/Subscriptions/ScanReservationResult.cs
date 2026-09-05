namespace AiVideoDetection.Application.Subscriptions;

public class ScanReservationResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public long? ReservationId { get; init; }
    public string EntitlementType { get; init; } = string.Empty;
    public bool ReservationRequired { get; init; } = true;
    public int? RemainingScans { get; init; }

    public static ScanReservationResult Succeeded(
        string message,
        string entitlementType,
        long? reservationId,
        int? remainingScans,
        bool reservationRequired = true)
    {
        return new ScanReservationResult
        {
            Success = true,
            Message = message,
            EntitlementType = entitlementType,
            ReservationId = reservationId,
            RemainingScans = remainingScans,
            ReservationRequired = reservationRequired
        };
    }

    public static ScanReservationResult Failed(string message, string errorCode)
    {
        return new ScanReservationResult
        {
            Success = false,
            Message = message,
            ErrorCode = errorCode
        };
    }
}
