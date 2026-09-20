using System.Collections.Concurrent;
using System.Data;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.DTOs;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Subscriptions.Options;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Subscriptions;

public class EntitlementService(
    AppDbContext dbContext,
    IOptions<ScanReservationOptions>? reservationOptions = null) : IEntitlementService
{
    private const int GuestUploadLimit = 1;
    private static readonly ConcurrentDictionary<string, ReservationLockEntry> ReservationLocks = new();
    private readonly ScanReservationOptions _reservationOptions = reservationOptions?.Value ?? new ScanReservationOptions();

    public async Task<ApiResponse<SubscriptionStatusResponse>> GetStatusAsync(
        long userId,
        SubscriptionClientContext? clientContext = null,
        CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return ApiResponse<SubscriptionStatusResponse>.ErrorResponse(
                "User was not found.",
                errorCode: SubscriptionErrorCodes.UserNotFound);
        }

        if (user.Role == UserRole.Admin)
        {
            return ApiResponse<SubscriptionStatusResponse>.SuccessResponse(new SubscriptionStatusResponse
            {
                PlanCode = ScanReservationKinds.InternalUnlimited,
                PlanName = "Internal unlimited",
                IsAdmin = true,
                SubscriptionStatus = SubscriptionStatuses.Active,
                AllowsSmartScan = true,
                AllowsDetailedScan = true,
                MaxVideoSizeBytes = VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes
            });
        }

        await ExpireSubscriptionsAsync(cancellationToken);

        var paidSubscription = await GetActivePaidSubscriptionAsync(userId, cancellationToken);
        if (paidSubscription is not null)
        {
            var status = await BuildPaidStatusAsync(paidSubscription, cancellationToken);
            return ApiResponse<SubscriptionStatusResponse>.SuccessResponse(status);
        }

        var freePlan = await EnsureGuestFreePlanAsync(cancellationToken);
        var freeStatus = await BuildFreeStatusAsync(userId, freePlan, clientContext, cancellationToken);
        return ApiResponse<SubscriptionStatusResponse>.SuccessResponse(freeStatus);
    }

    public async Task<ApiResponse<GuestUploadStatusResponse>> GetGuestUploadStatusAsync(
        SubscriptionClientContext? clientContext = null,
        CancellationToken cancellationToken = default)
    {
        var freePlan = await EnsureGuestFreePlanAsync(cancellationToken);

        if (clientContext?.DeviceIdentityId is null)
        {
            return ApiResponse<GuestUploadStatusResponse>.SuccessResponse(new GuestUploadStatusResponse
            {
                CanUpload = false,
                RemainingUploads = 0,
                BlockReasonCode = SubscriptionErrorCodes.DeviceIdentityRequired,
                MaxVideoSizeBytes = freePlan.MaxVideoSizeBytes
            });
        }

        if (string.IsNullOrWhiteSpace(clientContext.IpHash))
        {
            return ApiResponse<GuestUploadStatusResponse>.SuccessResponse(new GuestUploadStatusResponse
            {
                CanUpload = false,
                RemainingUploads = 0,
                BlockReasonCode = SubscriptionErrorCodes.ClientIpRequired,
                MaxVideoSizeBytes = freePlan.MaxVideoSizeBytes
            });
        }

        var deviceUsage = await dbContext.FreeTrialDeviceUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(usage => usage.DeviceIdentityId == clientContext.DeviceIdentityId.Value, cancellationToken);
        var ipUsage = await dbContext.FreeTrialIpUsages
            .AsNoTracking()
            .FirstOrDefaultAsync(usage => usage.IpHash == clientContext.IpHash, cancellationToken);
        var remaining = GetGuestRemainingUploads(deviceUsage, ipUsage);

        return ApiResponse<GuestUploadStatusResponse>.SuccessResponse(new GuestUploadStatusResponse
        {
            CanUpload = remaining > 0,
            RemainingUploads = remaining,
            BlockReasonCode = remaining > 0 ? null : SubscriptionErrorCodes.GuestLimitReached,
            MaxVideoSizeBytes = freePlan.MaxVideoSizeBytes
        });
    }

    public async Task<ScanReservationResult> ReserveScanAsync(
        ScanReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        var acquiredLocks = await AcquireReservationLocksAsync(request, cancellationToken);
        try
        {
            return await ReserveScanCoreAsync(request, cancellationToken);
        }
        finally
        {
            ReleaseReservationLocks(acquiredLocks);
        }
    }

    private async Task<ScanReservationResult> ReserveScanCoreAsync(
        ScanReservationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .FirstOrDefaultAsync(candidate => candidate.Id == request.UserId, cancellationToken);
        if (user is null)
        {
            return ScanReservationResult.Failed("User was not found.", SubscriptionErrorCodes.UserNotFound);
        }

        if (user.Role == UserRole.Admin)
        {
            return ScanReservationResult.Succeeded(
                "Admin scan bypasses subscription quota.",
                ScanReservationKinds.InternalUnlimited,
                reservationId: null,
                remainingScans: null,
                reservationRequired: false);
        }

        await ExpireSubscriptionsAsync(cancellationToken);

        var transaction = await BeginSerializableTransactionIfSupportedAsync(cancellationToken);
        await using (transaction)
        {
            var paidSubscription = await GetActivePaidSubscriptionAsync(request.UserId, cancellationToken);
            if (paidSubscription is not null)
            {
                var result = await ReservePaidScanAsync(paidSubscription, request, cancellationToken);
                if (transaction is not null && result.Success)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return result;
            }

            var freeResult = await ReserveFreeTrialScanAsync(request, cancellationToken);
            if (transaction is not null && freeResult.Success)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return freeResult;
        }
    }

    private static async Task<List<(string Key, ReservationLockEntry Entry)>> AcquireReservationLocksAsync(
        ScanReservationRequest request,
        CancellationToken cancellationToken)
    {
        var lockKeys = GetReservationLockKeys(request);
        var acquiredLocks = new List<(string Key, ReservationLockEntry Entry)>(lockKeys.Count);

        foreach (var lockKey in lockKeys)
        {
            var entry = ReservationLocks.GetOrAdd(lockKey, _ => new ReservationLockEntry());
            Interlocked.Increment(ref entry.ReferenceCount);
            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
            }
            catch
            {
                if (Interlocked.Decrement(ref entry.ReferenceCount) == 0)
                {
                    ReservationLocks.TryRemove(lockKey, out _);
                }

                throw;
            }

            acquiredLocks.Add((lockKey, entry));
        }

        return acquiredLocks;
    }

    private static List<string> GetReservationLockKeys(ScanReservationRequest request)
    {
        var lockKeys = new SortedSet<string>(StringComparer.Ordinal)
        {
            $"user:{request.UserId}"
        };

        if (request.ClientContext?.DeviceIdentityId is { } deviceIdentityId)
        {
            lockKeys.Add($"device:{deviceIdentityId}");
        }

        if (!string.IsNullOrWhiteSpace(request.ClientContext?.IpHash))
        {
            lockKeys.Add($"ip:{request.ClientContext.IpHash.Trim()}");
        }

        return lockKeys.ToList();
    }

    private static void ReleaseReservationLocks(List<(string Key, ReservationLockEntry Entry)> acquiredLocks)
    {
        for (var index = acquiredLocks.Count - 1; index >= 0; index--)
        {
            var (key, entry) = acquiredLocks[index];
            entry.Semaphore.Release();
            if (Interlocked.Decrement(ref entry.ReferenceCount) == 0)
            {
                ReservationLocks.TryRemove(key, out _);
            }
        }
    }

    private sealed class ReservationLockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);

        public int ReferenceCount;
    }

    public async Task<ScanReservationResult> ConsumeReservationAsync(
        long reservationId,
        long? videoId = null,
        long? analysisJobId = null,
        CancellationToken cancellationToken = default)
    {
        var transaction = await BeginSerializableTransactionIfSupportedAsync(cancellationToken);
        await using (transaction)
        {
            var reservation = await dbContext.ScanReservations
                .FirstOrDefaultAsync(candidate => candidate.Id == reservationId, cancellationToken);
            if (reservation is null)
            {
                return ScanReservationResult.Failed(
                    "Scan reservation was not found.",
                    SubscriptionErrorCodes.ReservationNotFound);
            }

            if (reservation.Status == ScanReservationStatuses.Consumed)
            {
                return ScanReservationResult.Succeeded(
                    "Scan reservation was already consumed.",
                    reservation.ReservationKind,
                    reservation.Id,
                    remainingScans: null);
            }

            if (reservation.Status == ScanReservationStatuses.Released)
            {
                return ScanReservationResult.Failed(
                    "Scan reservation was already released.",
                    SubscriptionErrorCodes.ReservationAlreadyReleased);
            }

            var now = DateTimeOffset.UtcNow;
            reservation.Status = ScanReservationStatuses.Consumed;
            reservation.ConsumedAt = now;
            reservation.VideoId = videoId ?? reservation.VideoId;
            reservation.AnalysisJobId = analysisJobId ?? reservation.AnalysisJobId;

            dbContext.ScanUsages.Add(new ScanUsage
            {
                UserId = reservation.UserId,
                UserSubscriptionId = reservation.UserSubscriptionId,
                SubscriptionPlanId = reservation.SubscriptionPlanId,
                ScanReservationId = reservation.Id,
                VideoId = reservation.VideoId,
                AnalysisJobId = reservation.AnalysisJobId,
                EntitlementType = reservation.ReservationKind,
                AnalysisMode = reservation.AnalysisMode,
                OccurredAt = now
            });

            if (reservation.ReservationKind == ScanReservationKinds.FreeTrial)
            {
                await MoveFreeTrialReservationToConsumedAsync(reservation, now, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ScanReservationResult.Succeeded(
                "Scan reservation consumed.",
                reservation.ReservationKind,
                reservation.Id,
                remainingScans: null);
        }
    }

    public async Task<ScanReservationResult> ReleaseReservationAsync(
        long reservationId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var transaction = await BeginSerializableTransactionIfSupportedAsync(cancellationToken);
        await using (transaction)
        {
            var reservation = await dbContext.ScanReservations
                .FirstOrDefaultAsync(candidate => candidate.Id == reservationId, cancellationToken);
            if (reservation is null)
            {
                return ScanReservationResult.Failed(
                    "Scan reservation was not found.",
                    SubscriptionErrorCodes.ReservationNotFound);
            }

            if (reservation.Status == ScanReservationStatuses.Released)
            {
                return ScanReservationResult.Succeeded(
                    "Scan reservation was already released.",
                    reservation.ReservationKind,
                    reservation.Id,
                    remainingScans: null);
            }

            if (reservation.Status == ScanReservationStatuses.Consumed)
            {
                return ScanReservationResult.Failed(
                    "Consumed scan reservations cannot be released.",
                    SubscriptionErrorCodes.ReservationCannotBeReleased);
            }

            var now = DateTimeOffset.UtcNow;
            reservation.Status = ScanReservationStatuses.Released;
            reservation.ReleasedAt = now;
            reservation.ReleaseReason = string.IsNullOrWhiteSpace(reason) ? "Released without consumption." : reason.Trim();

            if (reservation.ReservationKind == ScanReservationKinds.FreeTrial)
            {
                await ReleaseFreeTrialCountersAsync(reservation, now, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ScanReservationResult.Succeeded(
                "Scan reservation released.",
                reservation.ReservationKind,
                reservation.Id,
                remainingScans: null);
        }
    }

    private async Task<ScanReservationResult> ReservePaidScanAsync(
        UserSubscription subscription,
        ScanReservationRequest request,
        CancellationToken cancellationToken)
    {
        var plan = subscription.SubscriptionPlan;
        var validationError = ValidatePlanAccess(plan, request.AnalysisMode, request.FileSizeBytes);
        if (validationError is not null)
        {
            return validationError;
        }

        var usedScans = await dbContext.ScanUsages
            .CountAsync(usage => usage.UserSubscriptionId == subscription.Id, cancellationToken);
        var reservedScans = await dbContext.ScanReservations
            .CountAsync(reservation =>
                    reservation.UserSubscriptionId == subscription.Id &&
                    reservation.Status == ScanReservationStatuses.Reserved,
                cancellationToken);
        var remaining = plan.ScanLimit - usedScans - reservedScans;
        if (remaining <= 0)
        {
            return ScanReservationResult.Failed(
                "Subscription scan quota is exhausted.",
                SubscriptionErrorCodes.ScanQuotaExhausted);
        }

        var now = DateTimeOffset.UtcNow;
        var reservation = new ScanReservation
        {
            UserId = request.UserId,
            UserSubscriptionId = subscription.Id,
            SubscriptionPlanId = plan.Id,
            ReservationKind = ScanReservationKinds.PaidSubscription,
            Status = ScanReservationStatuses.Reserved,
            AnalysisMode = request.AnalysisMode.ToString(),
            FileSizeBytes = request.FileSizeBytes,
            ReservedAt = now,
            ExpiresAt = ResolveInitialExpiry(now),
            LastHeartbeatAt = now
        };

        dbContext.ScanReservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ScanReservationResult.Succeeded(
            "Subscription scan reserved.",
            ScanReservationKinds.PaidSubscription,
            reservation.Id,
            remaining - 1);
    }

    private async Task<ScanReservationResult> ReserveFreeTrialScanAsync(
        ScanReservationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ClientContext?.DeviceIdentityId is null)
        {
            return ScanReservationResult.Failed(
                "Device identity is required for free-trial scans.",
                SubscriptionErrorCodes.DeviceIdentityRequired);
        }

        if (string.IsNullOrWhiteSpace(request.ClientContext.IpHash))
        {
            return ScanReservationResult.Failed(
                "Client IP is required for free-trial scans.",
                SubscriptionErrorCodes.ClientIpRequired);
        }

        var freePlan = request.IsGuestUpload
            ? await EnsureGuestFreePlanAsync(cancellationToken)
            : await GetPlanAsync(SubscriptionPlanCodes.Free, cancellationToken);
        if (freePlan is null)
        {
            return ScanReservationResult.Failed(
                "Free-trial plan is not available.",
                SubscriptionErrorCodes.SubscriptionRequired);
        }

        var validationError = ValidatePlanAccess(freePlan, request.AnalysisMode, request.FileSizeBytes);
        if (validationError is not null)
        {
            return validationError;
        }

        var now = DateTimeOffset.UtcNow;
        var accountUsage = await GetOrCreateAccountUsageAsync(request.UserId, now, cancellationToken);
        var deviceUsage = await GetOrCreateDeviceUsageAsync(request.ClientContext.DeviceIdentityId.Value, now, cancellationToken);
        var ipUsage = await GetOrCreateIpUsageAsync(request.ClientContext.IpHash, now, cancellationToken);

        if (request.IsGuestUpload && GetGuestRemainingUploads(deviceUsage, ipUsage) <= 0)
        {
            return ScanReservationResult.Failed(
                "Guest upload limit reached. Please sign in or create an account to continue.",
                SubscriptionErrorCodes.GuestLimitReached);
        }

        var remaining = new[]
        {
            Remaining(accountUsage.AllocatedScans, accountUsage.ConsumedScans, accountUsage.ReservedScans),
            Remaining(deviceUsage.AllocatedScans, deviceUsage.ConsumedScans, deviceUsage.ReservedScans),
            Remaining(ipUsage.AllocatedScans, ipUsage.ConsumedScans, ipUsage.ReservedScans)
        }.Min();

        if (remaining <= 0)
        {
            return ScanReservationResult.Failed(
                "Free-trial scan quota is exhausted.",
                SubscriptionErrorCodes.FreeTrialExhausted);
        }

        accountUsage.ReservedScans++;
        accountUsage.FirstUsedAt ??= now;
        accountUsage.LastUsedAt = now;
        deviceUsage.ReservedScans++;
        deviceUsage.FirstUsedAt ??= now;
        deviceUsage.LastUsedAt = now;
        ipUsage.ReservedScans++;
        ipUsage.FirstUsedAt ??= now;
        ipUsage.LastUsedAt = now;

        var reservation = new ScanReservation
        {
            UserId = request.UserId,
            SubscriptionPlanId = freePlan.Id,
            DeviceIdentityId = request.ClientContext.DeviceIdentityId,
            FreeTrialIpHash = request.ClientContext.IpHash,
            ReservationKind = ScanReservationKinds.FreeTrial,
            Status = ScanReservationStatuses.Reserved,
            AnalysisMode = request.AnalysisMode.ToString(),
            FileSizeBytes = request.FileSizeBytes,
            ReservedAt = now,
            ExpiresAt = ResolveInitialExpiry(now),
            LastHeartbeatAt = now
        };

        dbContext.ScanReservations.Add(reservation);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ScanReservationResult.Succeeded(
            "Free-trial scan reserved.",
            ScanReservationKinds.FreeTrial,
            reservation.Id,
            remaining - 1);
    }

    private async Task<SubscriptionStatusResponse> BuildPaidStatusAsync(
        UserSubscription subscription,
        CancellationToken cancellationToken)
    {
        var usedScans = await dbContext.ScanUsages
            .CountAsync(usage => usage.UserSubscriptionId == subscription.Id, cancellationToken);
        var reservedScans = await dbContext.ScanReservations
            .CountAsync(reservation =>
                    reservation.UserSubscriptionId == subscription.Id &&
                    reservation.Status == ScanReservationStatuses.Reserved,
                cancellationToken);
        var plan = subscription.SubscriptionPlan;

        return new SubscriptionStatusResponse
        {
            PlanCode = plan.Code,
            PlanName = plan.Name,
            IsPaid = true,
            SubscriptionStatus = subscription.Status,
            StartsAt = subscription.StartsAt,
            ExpiresAt = subscription.ExpiresAt,
            ScanLimit = plan.ScanLimit,
            UsedScans = usedScans,
            ReservedScans = reservedScans,
            RemainingScans = Math.Max(0, plan.ScanLimit - usedScans - reservedScans),
            MaxVideoSizeBytes = plan.MaxVideoSizeBytes,
            AllowsSmartScan = plan.AllowsSmartScan,
            AllowsDetailedScan = plan.AllowsDetailedScan
        };
    }

    private async Task<SubscriptionStatusResponse> BuildFreeStatusAsync(
        long userId,
        SubscriptionPlan freePlan,
        SubscriptionClientContext? clientContext,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var accountUsage = await GetOrCreateAccountUsageAsync(userId, now, cancellationToken);
        FreeTrialDeviceUsage? deviceUsage = null;
        FreeTrialIpUsage? ipUsage = null;

        if (clientContext?.DeviceIdentityId is not null)
        {
            deviceUsage = await GetOrCreateDeviceUsageAsync(clientContext.DeviceIdentityId.Value, now, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(clientContext?.IpHash))
        {
            ipUsage = await GetOrCreateIpUsageAsync(clientContext.IpHash, now, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var accountRemaining = Remaining(accountUsage.AllocatedScans, accountUsage.ConsumedScans, accountUsage.ReservedScans);
        var deviceRemaining = deviceUsage is null
            ? (int?)null
            : Remaining(deviceUsage.AllocatedScans, deviceUsage.ConsumedScans, deviceUsage.ReservedScans);
        var ipRemaining = ipUsage is null
            ? (int?)null
            : Remaining(ipUsage.AllocatedScans, ipUsage.ConsumedScans, ipUsage.ReservedScans);
        var effectiveRemaining = new[] { accountRemaining, deviceRemaining, ipRemaining }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .DefaultIfEmpty(accountRemaining)
            .Min();

        return new SubscriptionStatusResponse
        {
            PlanCode = freePlan.Code,
            PlanName = freePlan.Name,
            SubscriptionStatus = SubscriptionStatuses.Active,
            ScanLimit = freePlan.ScanLimit,
            UsedScans = accountUsage.ConsumedScans,
            ReservedScans = accountUsage.ReservedScans,
            RemainingScans = effectiveRemaining,
            MaxVideoSizeBytes = freePlan.MaxVideoSizeBytes,
            AllowsSmartScan = freePlan.AllowsSmartScan,
            AllowsDetailedScan = freePlan.AllowsDetailedScan,
            FreeTrial = new FreeTrialStatusDto
            {
                AccountRemainingScans = accountRemaining,
                DeviceRemainingScans = deviceRemaining,
                IpRemainingScans = ipRemaining,
                EffectiveRemainingScans = effectiveRemaining
            }
        };
    }

    private async Task<UserSubscription?> GetActivePaidSubscriptionAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        return await dbContext.UserSubscriptions
            .Include(subscription => subscription.SubscriptionPlan)
            .Where(subscription =>
                subscription.UserId == userId &&
                subscription.Status == SubscriptionStatuses.Active &&
                subscription.StartsAt <= now &&
                subscription.ExpiresAt > now &&
                subscription.SubscriptionPlan.Code != SubscriptionPlanCodes.Free &&
                subscription.SubscriptionPlan.IsActive)
            .OrderByDescending(subscription => subscription.ExpiresAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<SubscriptionPlan?> GetPlanAsync(string planCode, CancellationToken cancellationToken)
    {
        return await dbContext.SubscriptionPlans
            .FirstOrDefaultAsync(plan => plan.Code == planCode && plan.IsActive, cancellationToken);
    }

    private async Task<SubscriptionPlan> EnsureGuestFreePlanAsync(CancellationToken cancellationToken)
    {
        var freePlan = await GetPlanAsync(SubscriptionPlanCodes.Free, cancellationToken);
        if (freePlan is not null)
        {
            return freePlan;
        }

        freePlan = new SubscriptionPlan
        {
            Code = SubscriptionPlanCodes.Free,
            Name = "Free",
            PriceAmount = 0m,
            Currency = "PKR",
            ScanLimit = 2,
            MaxVideoSizeBytes = VideoUploadSizeLimits.FreeMaxVideoSizeBytes,
            AllowsSmartScan = true,
            AllowsDetailedScan = false,
            ValidityDays = null,
            IsActive = true,
            SortOrder = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        dbContext.SubscriptionPlans.Add(freePlan);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(freePlan).State = EntityState.Detached;
            freePlan = await GetPlanAsync(SubscriptionPlanCodes.Free, cancellationToken);
            if (freePlan is null)
            {
                throw;
            }
        }

        return freePlan;
    }

    private async Task ExpireSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredSubscriptions = await dbContext.UserSubscriptions
            .Where(subscription =>
                subscription.Status == SubscriptionStatuses.Active &&
                subscription.ExpiresAt <= now)
            .ToListAsync(cancellationToken);

        if (expiredSubscriptions.Count == 0)
        {
            return;
        }

        foreach (var subscription in expiredSubscriptions)
        {
            subscription.Status = SubscriptionStatuses.Expired;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private ScanReservationResult? ValidatePlanAccess(
        SubscriptionPlan plan,
        AnalysisMode analysisMode,
        long fileSizeBytes)
    {
        if (analysisMode == AnalysisMode.Detailed && !plan.AllowsDetailedScan)
        {
            return ScanReservationResult.Failed(
                "Detailed scans are not included in this plan.",
                SubscriptionErrorCodes.DetailedScanNotAllowed);
        }

        if (analysisMode == AnalysisMode.Basic && !plan.AllowsSmartScan)
        {
            return ScanReservationResult.Failed(
                "Smart scans are not included in this plan.",
                SubscriptionErrorCodes.SubscriptionRequired);
        }

        if (fileSizeBytes > plan.MaxVideoSizeBytes)
        {
            return ScanReservationResult.Failed(
                GetFileSizeLimitMessage(plan),
                SubscriptionErrorCodes.VideoSizeLimitExceeded);
        }

        return null;
    }

    private static string GetFileSizeLimitMessage(SubscriptionPlan plan)
    {
        return plan.Code switch
        {
            SubscriptionPlanCodes.Free => "Your Free plan supports videos up to 200 MB. Upgrade your plan to analyze larger videos.",
            SubscriptionPlanCodes.Plus => "Your Plus plan supports videos up to 250 MB. Upgrade to Pro to analyze videos up to 300 MB.",
            SubscriptionPlanCodes.Pro => "Your Pro plan supports videos up to 300 MB.",
            _ => "Videos larger than 300 MB are not supported at this time."
        };
    }

    private async Task<FreeTrialAccountUsage> GetOrCreateAccountUsageAsync(
        long userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var usage = await dbContext.FreeTrialAccountUsages
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);
        if (usage is not null)
        {
            return usage;
        }

        usage = new FreeTrialAccountUsage
        {
            UserId = userId,
            AllocatedScans = 2,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.FreeTrialAccountUsages.Add(usage);
        return usage;
    }

    private async Task<FreeTrialDeviceUsage> GetOrCreateDeviceUsageAsync(
        long deviceIdentityId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var usage = await dbContext.FreeTrialDeviceUsages
            .FirstOrDefaultAsync(candidate => candidate.DeviceIdentityId == deviceIdentityId, cancellationToken);
        if (usage is not null)
        {
            return usage;
        }

        usage = new FreeTrialDeviceUsage
        {
            DeviceIdentityId = deviceIdentityId,
            AllocatedScans = 2,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.FreeTrialDeviceUsages.Add(usage);
        return usage;
    }

    private async Task<FreeTrialIpUsage> GetOrCreateIpUsageAsync(
        string ipHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var usage = await dbContext.FreeTrialIpUsages
            .FirstOrDefaultAsync(candidate => candidate.IpHash == ipHash, cancellationToken);
        if (usage is not null)
        {
            return usage;
        }

        usage = new FreeTrialIpUsage
        {
            IpHash = ipHash,
            HashVersion = "hmac-sha256-v1",
            AllocatedScans = 2,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.FreeTrialIpUsages.Add(usage);
        return usage;
    }

    private async Task MoveFreeTrialReservationToConsumedAsync(
        ScanReservation reservation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accountUsage = await dbContext.FreeTrialAccountUsages
            .FirstAsync(usage => usage.UserId == reservation.UserId, cancellationToken);
        accountUsage.ReservedScans = Math.Max(0, accountUsage.ReservedScans - 1);
        accountUsage.ConsumedScans++;
        accountUsage.LastUsedAt = now;

        if (reservation.DeviceIdentityId is not null)
        {
            var deviceUsage = await dbContext.FreeTrialDeviceUsages
                .FirstAsync(usage => usage.DeviceIdentityId == reservation.DeviceIdentityId, cancellationToken);
            deviceUsage.ReservedScans = Math.Max(0, deviceUsage.ReservedScans - 1);
            deviceUsage.ConsumedScans++;
            deviceUsage.LastUsedAt = now;
        }

        if (!string.IsNullOrWhiteSpace(reservation.FreeTrialIpHash))
        {
            var ipUsage = await dbContext.FreeTrialIpUsages
                .FirstAsync(usage => usage.IpHash == reservation.FreeTrialIpHash, cancellationToken);
            ipUsage.ReservedScans = Math.Max(0, ipUsage.ReservedScans - 1);
            ipUsage.ConsumedScans++;
            ipUsage.LastUsedAt = now;
        }
    }

    private async Task ReleaseFreeTrialCountersAsync(
        ScanReservation reservation,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var accountUsage = await dbContext.FreeTrialAccountUsages
            .FirstAsync(usage => usage.UserId == reservation.UserId, cancellationToken);
        accountUsage.ReservedScans = Math.Max(0, accountUsage.ReservedScans - 1);
        accountUsage.LastUsedAt = now;

        if (reservation.DeviceIdentityId is not null)
        {
            var deviceUsage = await dbContext.FreeTrialDeviceUsages
                .FirstAsync(usage => usage.DeviceIdentityId == reservation.DeviceIdentityId, cancellationToken);
            deviceUsage.ReservedScans = Math.Max(0, deviceUsage.ReservedScans - 1);
            deviceUsage.LastUsedAt = now;
        }

        if (!string.IsNullOrWhiteSpace(reservation.FreeTrialIpHash))
        {
            var ipUsage = await dbContext.FreeTrialIpUsages
                .FirstAsync(usage => usage.IpHash == reservation.FreeTrialIpHash, cancellationToken);
            ipUsage.ReservedScans = Math.Max(0, ipUsage.ReservedScans - 1);
            ipUsage.LastUsedAt = now;
        }
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionIfSupportedAsync(
        CancellationToken cancellationToken)
    {
        return dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;
    }

    private DateTimeOffset ResolveInitialExpiry(DateTimeOffset now)
    {
        return now.AddMinutes(Math.Max(1, _reservationOptions.UnlinkedReservationTtlMinutes));
    }

    private static int Remaining(int allocatedScans, int consumedScans, int reservedScans)
    {
        return Math.Max(0, allocatedScans - consumedScans - reservedScans);
    }

    private static int GetGuestRemainingUploads(FreeTrialDeviceUsage? deviceUsage, FreeTrialIpUsage? ipUsage)
    {
        var usedUploads = new[]
            {
                deviceUsage is null ? 0 : deviceUsage.ConsumedScans + deviceUsage.ReservedScans,
                ipUsage is null ? 0 : ipUsage.ConsumedScans + ipUsage.ReservedScans
            }
            .Max();

        return Math.Max(0, GuestUploadLimit - usedUploads);
    }
}
