using AiVideoDetection.Application.Subscriptions.DTOs;

namespace AiVideoDetection.Application.Subscriptions.Interfaces;

public interface IScanReservationReconciliationService
{
    Task<ScanReservationReconciliationResult> ReconcileAsync();

    Task<ScanReservationReconciliationResult> ReconcileAsync(CancellationToken cancellationToken);
}
