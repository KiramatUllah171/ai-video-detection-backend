namespace AiVideoDetection.Application.Subscriptions.Options;

public sealed class ScanReservationOptions
{
    public const string SectionName = "ScanReservations";

    public int UnlinkedReservationTtlMinutes { get; init; } = 60;

    public int ActiveJobSafetyMarginMinutes { get; init; } = 5;

    public int ReconciliationBatchSize { get; init; } = 100;
}
