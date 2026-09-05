namespace AiVideoDetection.Application.Subscriptions.DTOs;

public sealed class ScanReservationReconciliationResult
{
    public int CheckedReservations { get; init; }

    public int ConsumedReservations { get; init; }

    public int ReleasedReservations { get; init; }

    public int FailedStaleJobs { get; init; }

    public int SkippedActiveReservations { get; init; }

    public int AlreadyFinalizedReservations { get; init; }
}
