using AiVideoDetection.Application.Subscriptions.DTOs;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Subscriptions.Options;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Subscriptions;

public sealed class ScanReservationReconciliationService(
    AppDbContext dbContext,
    IEntitlementService entitlementService,
    IOptions<ScanReservationOptions> reservationOptions,
    IOptions<VideoProcessingOptions> processingOptions,
    ILogger<ScanReservationReconciliationService> logger) : IScanReservationReconciliationService
{
    private readonly ScanReservationOptions _reservationOptions = reservationOptions.Value;
    private readonly VideoProcessingOptions _processingOptions = processingOptions.Value;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public Task<ScanReservationReconciliationResult> ReconcileAsync()
    {
        return ReconcileAsync(CancellationToken.None);
    }

    public async Task<ScanReservationReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var activeStaleCutoff = now.Subtract(ResolveActiveJobStaleWindow());
        var unlinkedExpiryCutoff = now.Subtract(TimeSpan.FromMinutes(Math.Max(1, _reservationOptions.UnlinkedReservationTtlMinutes)));
        var checkedReservations = 0;
        var consumedReservations = 0;
        var releasedReservations = 0;
        var failedStaleJobs = 0;
        var skippedActiveReservations = 0;
        var alreadyFinalizedReservations = 0;

        var reservationIds = await dbContext.ScanReservations
            .AsNoTracking()
            .Where(reservation => reservation.Status == ScanReservationStatuses.Reserved)
            .OrderBy(reservation => reservation.ReservedAt)
            .Select(reservation => reservation.Id)
            .Take(Math.Max(1, _reservationOptions.ReconciliationBatchSize))
            .ToListAsync(cancellationToken);

        foreach (var reservationId in reservationIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkedReservations++;

            var action = await ReconcileReservationAsync(reservationId, now, unlinkedExpiryCutoff, activeStaleCutoff, cancellationToken);
            switch (action)
            {
                case ReconciliationAction.Consumed:
                    consumedReservations++;
                    break;
                case ReconciliationAction.Released:
                    releasedReservations++;
                    break;
                case ReconciliationAction.FailedStaleJobAndReleased:
                    failedStaleJobs++;
                    releasedReservations++;
                    break;
                case ReconciliationAction.SkippedActive:
                    skippedActiveReservations++;
                    break;
                case ReconciliationAction.AlreadyFinalized:
                    alreadyFinalizedReservations++;
                    break;
            }
        }

        if (checkedReservations > 0)
        {
            logger.LogInformation(
                "Scan reservation reconciliation checked {CheckedReservations}, consumed {ConsumedReservations}, released {ReleasedReservations}, failed stale jobs {FailedStaleJobs}, skipped active {SkippedActiveReservations}, already finalized {AlreadyFinalizedReservations}.",
                checkedReservations,
                consumedReservations,
                releasedReservations,
                failedStaleJobs,
                skippedActiveReservations,
                alreadyFinalizedReservations);
        }

        return new ScanReservationReconciliationResult
        {
            CheckedReservations = checkedReservations,
            ConsumedReservations = consumedReservations,
            ReleasedReservations = releasedReservations,
            FailedStaleJobs = failedStaleJobs,
            SkippedActiveReservations = skippedActiveReservations,
            AlreadyFinalizedReservations = alreadyFinalizedReservations
        };
    }

    private async Task<ReconciliationAction> ReconcileReservationAsync(
        long reservationId,
        DateTimeOffset now,
        DateTimeOffset unlinkedExpiryCutoff,
        DateTimeOffset activeStaleCutoff,
        CancellationToken cancellationToken)
    {
        var reservation = await dbContext.ScanReservations
            .AsNoTracking()
            .Include(candidate => candidate.AnalysisJob)
            .FirstOrDefaultAsync(candidate => candidate.Id == reservationId, cancellationToken);

        if (reservation is null || reservation.Status != ScanReservationStatuses.Reserved)
        {
            return ReconciliationAction.AlreadyFinalized;
        }

        if (reservation.AnalysisJob is null)
        {
            var expiresAt = reservation.ExpiresAt ?? reservation.ReservedAt.AddMinutes(Math.Max(1, _reservationOptions.UnlinkedReservationTtlMinutes));
            if (expiresAt <= now || reservation.ReservedAt <= unlinkedExpiryCutoff)
            {
                var release = await entitlementService.ReleaseReservationAsync(
                    reservation.Id,
                    "Reservation reconciliation released an expired reservation that was not linked to an analysis job.",
                    cancellationToken);
                return release.Success ? ReconciliationAction.Released : ReconciliationAction.AlreadyFinalized;
            }

            return ReconciliationAction.SkippedActive;
        }

        var job = reservation.AnalysisJob;
        if (job.Status == JobStatus.Completed)
        {
            var consume = await entitlementService.ConsumeReservationAsync(reservation.Id, job.VideoId, job.Id, cancellationToken);
            return consume.Success ? ReconciliationAction.Consumed : ReconciliationAction.AlreadyFinalized;
        }

        if (job.Status is JobStatus.Failed or JobStatus.Cancelled)
        {
            var release = await entitlementService.ReleaseReservationAsync(
                reservation.Id,
                $"Reservation reconciliation released a reservation for terminal {job.Status} analysis job {job.Id}.",
                cancellationToken);
            return release.Success ? ReconciliationAction.Released : ReconciliationAction.AlreadyFinalized;
        }

        if (job.Status == JobStatus.Paused)
        {
            return ReconciliationAction.SkippedActive;
        }

        if (IsActive(job.Status) && ResolveLastActivity(job) <= activeStaleCutoff)
        {
            var staleJob = await dbContext.AnalysisJobs
                .Include(candidate => candidate.Video)
                .FirstOrDefaultAsync(candidate => candidate.Id == job.Id, cancellationToken);

            if (staleJob is null || !IsActive(staleJob.Status) || ResolveLastActivity(staleJob) > activeStaleCutoff)
            {
                return ReconciliationAction.SkippedActive;
            }

            staleJob.Status = JobStatus.Failed;
            staleJob.Progress = Math.Clamp(staleJob.Progress, 0, 99);
            staleJob.CurrentStep = "Failed: Analysis job became stale and was recovered by reservation reconciliation.";
            staleJob.ErrorCode = "STALE_ANALYSIS_JOB";
            staleJob.ErrorMessage = "Analysis job became stale and was recovered safely. Please retry.";
            staleJob.FailedStage = "ReservationReconciliation";
            staleJob.FailedAt = now;
            staleJob.CompletedAt ??= now;
            staleJob.LastActivityAt = now;
            if (staleJob.Video is not null && staleJob.Video.Status != VideoStatus.Deleted)
            {
                staleJob.Video.Status = VideoStatus.Failed;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            var release = await entitlementService.ReleaseReservationAsync(
                reservation.Id,
                $"Reservation reconciliation released a reservation for stale analysis job {job.Id}.",
                cancellationToken);
            return release.Success ? ReconciliationAction.FailedStaleJobAndReleased : ReconciliationAction.AlreadyFinalized;
        }

        return ReconciliationAction.SkippedActive;
    }

    private TimeSpan ResolveActiveJobStaleWindow()
    {
        var processingMinutes = Math.Max(_processingOptions.WorkerTimeoutMinutes, _processingOptions.StaleActiveJobTimeoutMinutes);
        var totalMinutes = processingMinutes + Math.Max(1, _reservationOptions.ActiveJobSafetyMarginMinutes);
        return TimeSpan.FromMinutes(totalMinutes);
    }

    private static DateTimeOffset ResolveLastActivity(AiVideoDetection.Domain.Entities.AnalysisJob job)
    {
        return job.LastActivityAt ?? job.StartedAt ?? job.CreatedAt;
    }

    private static bool IsActive(JobStatus status)
    {
        return status is JobStatus.Queued
            or JobStatus.Preparing
            or JobStatus.Processing
            or JobStatus.Retrying
            or JobStatus.PauseRequested
            or JobStatus.ResumeRequested
            or JobStatus.CancelRequested;
    }

    private enum ReconciliationAction
    {
        SkippedActive,
        AlreadyFinalized,
        Consumed,
        Released,
        FailedStaleJobAndReleased
    }
}
