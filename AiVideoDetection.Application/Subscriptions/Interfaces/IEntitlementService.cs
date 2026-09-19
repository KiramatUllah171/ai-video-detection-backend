using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Subscriptions.DTOs;

namespace AiVideoDetection.Application.Subscriptions.Interfaces;

public interface IEntitlementService
{
    Task<ApiResponse<SubscriptionStatusResponse>> GetStatusAsync(
        long userId,
        SubscriptionClientContext? clientContext = null,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<GuestUploadStatusResponse>> GetGuestUploadStatusAsync(
        SubscriptionClientContext? clientContext = null,
        CancellationToken cancellationToken = default);

    Task<ScanReservationResult> ReserveScanAsync(
        ScanReservationRequest request,
        CancellationToken cancellationToken = default);

    Task<ScanReservationResult> ConsumeReservationAsync(
        long reservationId,
        long? videoId = null,
        long? analysisJobId = null,
        CancellationToken cancellationToken = default);

    Task<ScanReservationResult> ReleaseReservationAsync(
        long reservationId,
        string? reason = null,
        CancellationToken cancellationToken = default);
}
