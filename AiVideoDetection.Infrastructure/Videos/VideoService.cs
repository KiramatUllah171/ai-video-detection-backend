using System.Security.Cryptography;
using System.Text.Json;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Videos;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Processing;
using AiVideoDetection.Infrastructure.Videos.Processing;

namespace AiVideoDetection.Infrastructure.Videos;

public class VideoService(
    AppDbContext dbContext,
    IObjectStorageService objectStorageService,
    IAnalysisJobQueue analysisJobQueue,
    IJobLogService jobLogService,
    IEntitlementService entitlementService,
    IDeviceIdentityService deviceIdentityService,
    IMetadataExtractionService metadataExtractionService,
    IVideoStorageCapacityService storageCapacityService,
    IVideoWorkloadGate workloadGate,
    IValidator<UploadVideoRequest> uploadValidator,
    IOptions<VideoUploadOptions> uploadOptions,
    IOptions<VideoStorageProtectionOptions> storageProtectionOptions,
    IOptions<VideoProcessingOptions> processingOptions,
    ILogger<VideoService> logger) : IVideoService
{
    private readonly VideoUploadOptions _uploadOptions = uploadOptions.Value;
    private readonly VideoStorageProtectionOptions _storageProtectionOptions = storageProtectionOptions.Value;
    private readonly VideoProcessingOptions _processingOptions = processingOptions.Value;

    public async Task<ApiResponse<UploadVideoResponse>> UploadAsync(
        UploadVideoRequest request,
        long currentUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        return await UploadCoreAsync(request, currentUserId, ipAddress, isGuestUpload: false, cancellationToken);
    }

    private async Task<ApiResponse<UploadVideoResponse>> UploadCoreAsync(
        UploadVideoRequest request,
        long currentUserId,
        string? ipAddress,
        bool isGuestUpload,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await uploadValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Validation failed.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var file = request.File!;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var originalName = SanitizeOriginalName(file.FileName);
        var objectKey = CreateObjectKey(currentUserId, extension);
        string? uploadedObjectKey = null;
        var databaseCommitted = false;
        long? reservationId = null;
        var reservationReleased = false;
        string? tempFilePath = null;

        await using var uploadLease = await workloadGate.TryEnterUploadAsync(cancellationToken);
        if (uploadLease is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "The upload service is busy. Please try again shortly.",
                errorCode: VideoInfrastructureErrorCodes.UploadConcurrencyLimitReached);
        }

        var capacity = await storageCapacityService.CheckUploadCapacityAsync(file.Length, cancellationToken);
        if (!capacity.HasCapacity)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                capacity.Message ?? "The server is temporarily unable to accept this video. Please try again later.",
                errorCode: capacity.ErrorCode ?? VideoInfrastructureErrorCodes.ServerStorageCapacityLow);
        }

        try
        {
            var uploadTempRoot = Path.GetFullPath(_storageProtectionOptions.UploadTempRootPath);
            Directory.CreateDirectory(uploadTempRoot);
            tempFilePath = Path.Combine(uploadTempRoot, $"upload-{Guid.NewGuid():N}{extension}");

            string sha256Hash;
            await using (var tempFile = File.Create(tempFilePath))
            await using (var input = file.OpenReadStream())
            using (var sha256 = SHA256.Create())
            {
                var buffer = new byte[81920];
                int bytesRead;
                while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await tempFile.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                }

                sha256.TransformFinalBlock([], 0, 0);
                sha256Hash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            }

            if (!await HasSupportedVideoContentAsync(tempFilePath, extension, cancellationToken))
            {
                return ApiResponse<UploadVideoResponse>.ErrorResponse(
                    "The uploaded file content does not match a supported video format.");
            }

            var now = DateTimeOffset.UtcNow;
            var reportCutoff = now.Subtract(_processingOptions.ReportRetention);
            var videoRetentionCutoff = now.Subtract(_processingOptions.OriginalVideoRetention);
            var duplicateVideo = await dbContext.Videos
                .Include(video => video.AnalysisJobs)
                .Where(video => video.UserId == currentUserId
                    && video.DeletedAt == null
                    && video.Status != VideoStatus.Deleted
                    && video.Sha256Hash == sha256Hash
                    && ((video.RetentionDeleteAt != null && video.RetentionDeleteAt > now)
                        || (video.RetentionDeleteAt == null && video.CreatedAt > videoRetentionCutoff))
                    && (video.FileUrl != string.Empty || video.AiResults.Any(result => result.CreatedAt > reportCutoff)))
                .OrderByDescending(video => video.CreatedAt)
                .ThenByDescending(video => video.Id)
                .FirstOrDefaultAsync(cancellationToken);

            var duplicateJob = duplicateVideo?.AnalysisJobs
                .OrderByDescending(job => job.CreatedAt)
                .ThenByDescending(job => job.Id)
                .FirstOrDefault();
            if (duplicateVideo is not null && duplicateJob is not null)
            {
                logger.LogInformation(
                    "Duplicate upload prevented for user id {UserId}; existing video id {VideoId} reused.",
                    currentUserId,
                    duplicateVideo.Id);

                return ApiResponse<UploadVideoResponse>.SuccessResponse(
                    ToUploadResponse(duplicateVideo, duplicateJob, "This video has already been uploaded. Use the existing analysis record."),
                    "Video already uploaded.");
            }

            var clientContextResult = await TryResolveClientContextAsync(currentUserId, cancellationToken);
            if (clientContextResult.ErrorResponse is not null)
            {
                return clientContextResult.ErrorResponse;
            }

            var reservation = await entitlementService.ReserveScanAsync(new ScanReservationRequest
            {
                UserId = currentUserId,
                AnalysisMode = request.AnalysisMode,
                FileSizeBytes = file.Length,
                ClientContext = clientContextResult.Context,
                IsGuestUpload = isGuestUpload
            }, cancellationToken);
            if (!reservation.Success)
            {
                return ToUploadErrorResponse(reservation);
            }

            reservationId = reservation.ReservationId;

            if (string.Equals(file.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Video upload accepted with application/octet-stream for user id {UserId}.", currentUserId);
            }

            await using (var uploadStream = File.OpenRead(tempFilePath))
            {
                uploadedObjectKey = await objectStorageService.UploadAsync(
                    uploadStream,
                    objectKey,
                    file.ContentType,
                    cancellationToken);
            }

            var video = new Video
            {
                UserId = currentUserId,
                OriginalName = originalName,
                FileUrl = uploadedObjectKey,
                ContentType = file.ContentType,
                FileExtension = extension,
                FileSize = file.Length,
                Sha256Hash = sha256Hash,
                Status = VideoStatus.Uploaded,
                RetentionDeleteAt = DateTimeOffset.UtcNow.Add(_processingOptions.OriginalVideoRetention)
            };

            var job = new AnalysisJob
            {
                Video = video,
                Status = JobStatus.Queued,
                Progress = 0,
                CurrentStep = "Waiting for processing worker",
                RetryCount = 0,
                MaxRetryCount = _processingOptions.UserRetryLimit,
                ScanMode = request.AnalysisMode.ToString(),
                LastActivityAt = DateTimeOffset.UtcNow
            };

            if (dbContext.Database.IsRelational())
            {
                await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                dbContext.Videos.Add(video);
                dbContext.AnalysisJobs.Add(job);
                await dbContext.SaveChangesAsync(cancellationToken);
                await LinkReservationToJobAsync(reservationId, video.Id, job.Id, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                databaseCommitted = true;
            }
            else
            {
                dbContext.Videos.Add(video);
                dbContext.AnalysisJobs.Add(job);
                await dbContext.SaveChangesAsync(cancellationToken);
                await LinkReservationToJobAsync(reservationId, video.Id, job.Id, cancellationToken);
                databaseCommitted = true;
            }

            string backgroundJobId;
            try
            {
                backgroundJobId = analysisJobQueue.EnqueueAnalysisJob(job.Id);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to enqueue analysis job {AnalysisJobId} for uploaded video {VideoId}.", job.Id, video.Id);
                await MarkQueueFailureAsync(
                    video,
                    job,
                    reservationId,
                    "Upload failed before analysis could be queued.",
                    deleteUploadedObjectKey: uploadedObjectKey,
                    previousVideoStatus: VideoStatus.Failed);
                reservationReleased = true;

                return ApiResponse<UploadVideoResponse>.ErrorResponse(
                    "Analysis queue is temporarily unavailable. Please try uploading again shortly.",
                    errorCode: VideoInfrastructureErrorCodes.AnalysisQueueUnavailable);
            }

            logger.LogInformation(
                "Enqueued analysis job {AnalysisJobId} as Hangfire job {BackgroundJobId}.",
                job.Id,
                backgroundJobId);

            return ApiResponse<UploadVideoResponse>.SuccessResponse(
                ToUploadResponse(video, job, "Video uploaded and queued for processing."),
                "Video uploaded successfully.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (reservationId is not null && !reservationReleased)
            {
                await entitlementService.ReleaseReservationAsync(
                    reservationId.Value,
                    "Upload failed because server temporary storage was unavailable.",
                    CancellationToken.None);
                reservationReleased = true;
            }

            if (!databaseCommitted && !string.IsNullOrWhiteSpace(uploadedObjectKey))
            {
                await objectStorageService.DeleteAsync(uploadedObjectKey, CancellationToken.None);
            }

            logger.LogWarning(exception, "Video upload failed because server storage was unavailable.");
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "The server is temporarily unable to accept this video. Please try again later.",
                errorCode: VideoInfrastructureErrorCodes.ServerStorageCapacityLow);
        }
        catch
        {
            if (reservationId is not null && !reservationReleased)
            {
                await entitlementService.ReleaseReservationAsync(
                    reservationId.Value,
                    "Upload failed before analysis could be queued.",
                    CancellationToken.None);
                reservationReleased = true;
            }

            if (!databaseCommitted && !string.IsNullOrWhiteSpace(uploadedObjectKey))
            {
                await objectStorageService.DeleteAsync(uploadedObjectKey, cancellationToken);
            }

            throw;
        }
        finally
        {
            TryDeleteFile(tempFilePath);
        }
    }

    public async Task<ApiResponse<UploadVideoResponse>> UploadGuestAsync(
        UploadVideoRequest request,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (request.AnalysisMode != AnalysisMode.Basic)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Please create an account or sign in to use Detailed Scan.");
        }

        var validationResult = await uploadValidator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Validation failed.",
                validationResult.Errors.Select(error => error.ErrorMessage));
        }

        var guestContextResult = await TryResolveAnonymousClientContextAsync(cancellationToken);
        if (guestContextResult.ErrorResponse is not null)
        {
            return guestContextResult.ErrorResponse;
        }

        var guestStatus = await entitlementService.GetGuestUploadStatusAsync(guestContextResult.Context, cancellationToken);
        if (!guestStatus.Success || guestStatus.Data is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                guestStatus.Message,
                guestStatus.Errors,
                errorCode: guestStatus.ErrorCode);
        }

        if (!guestStatus.Data.CanUpload)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Guest upload limit reached. Please sign in or create an account to continue.",
                errorCode: guestStatus.Data.BlockReasonCode ?? SubscriptionErrorCodes.GuestLimitReached);
        }

        var token = GuestVideoAccessToken.Generate();
        var tokenHash = GuestVideoAccessToken.Hash(token);
        var guestId = Guid.NewGuid().ToString("N");
        var guestEmail = $"guest-{guestId}@guest.sachai.invalid";
        var guestUser = new User
        {
            Name = "Guest user",
            Email = guestEmail,
            NormalizedEmail = guestEmail.ToUpperInvariant(),
            UserName = guestEmail,
            NormalizedUserName = guestEmail.ToUpperInvariant(),
            PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            Role = UserRole.User,
            IsActive = true,
            EmailConfirmed = false,
            LockoutEnabled = true
        };

        dbContext.Users.Add(guestUser);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await UploadCoreAsync(request, guestUser.Id, ipAddress, isGuestUpload: true, cancellationToken);
        if (!response.Success || response.Data is null)
        {
            return response;
        }

        var video = await dbContext.Videos
            .FirstOrDefaultAsync(candidate => candidate.Id == response.Data.VideoId && candidate.UserId == guestUser.Id, cancellationToken);
        if (video is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Guest upload could not be prepared.");
        }

        video.GuestAccessTokenHash = tokenHash;
        video.GuestAccessExpiresAt = DateTimeOffset.UtcNow.Add(_processingOptions.OriginalVideoRetention);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<UploadVideoResponse>.SuccessResponse(
            ToGuestUploadResponse(response.Data, token),
            response.Message);
    }

    public async Task<ApiResponse<bool>> ClaimGuestVideoAsync(
        long videoId,
        long currentUserId,
        string guestAccessToken,
        CancellationToken cancellationToken = default)
    {
        if (!GuestVideoAccessToken.IsValid(guestAccessToken))
        {
            return ApiResponse<bool>.ErrorResponse("Guest upload access has expired or is invalid.");
        }

        var video = await dbContext.Videos
            .FirstOrDefaultAsync(candidate => candidate.Id == videoId
                && candidate.DeletedAt == null
                && candidate.Status != VideoStatus.Deleted,
                cancellationToken);
        if (video is null)
        {
            return ApiResponse<bool>.ErrorResponse("Video was not found.");
        }

        if (video.UserId == currentUserId)
        {
            return ApiResponse<bool>.SuccessResponse(true, "Video is already linked to this account.");
        }

        if (string.IsNullOrWhiteSpace(video.GuestAccessTokenHash)
            || !string.Equals(video.GuestAccessTokenHash, GuestVideoAccessToken.Hash(guestAccessToken), StringComparison.Ordinal)
            || video.GuestAccessExpiresAt <= DateTimeOffset.UtcNow)
        {
            return ApiResponse<bool>.ErrorResponse("Guest upload access has expired or is invalid.");
        }

        var userExists = await dbContext.Users.AnyAsync(user => user.Id == currentUserId && user.IsActive, cancellationToken);
        if (!userExists)
        {
            return ApiResponse<bool>.ErrorResponse("User was not found.");
        }

        video.UserId = currentUserId;
        video.GuestClaimedAt = DateTimeOffset.UtcNow;
        video.GuestAccessTokenHash = null;
        video.GuestAccessExpiresAt = null;
        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Video linked to your account.");
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Upload temp cleanup is best effort; retention cleanup removes stale files after crashes.
        }
    }

    public async Task<ApiResponse<PagedResponse<VideoHistoryItemDto>>> GetHistoryAsync(
        long currentUserId,
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTimeOffset.UtcNow;
        var reportCutoff = now.Subtract(_processingOptions.ReportRetention);

        var query = dbContext.Videos
            .AsNoTracking()
            .Where(video => video.UserId == currentUserId
                && video.DeletedAt == null
                && video.Status != VideoStatus.Deleted
                && (video.FileUrl != string.Empty || video.AiResults.Any(result => result.CreatedAt > reportCutoff)));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<VideoStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(video => video.Status == parsedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(video => video.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(video => new VideoHistoryItemDto
            {
                VideoId = video.Id,
                OriginalName = video.OriginalName,
                FileSize = video.FileSize,
                ContentType = video.ContentType,
                FileExtension = video.FileExtension,
                Status = video.Status.ToString(),
                CreatedAt = video.CreatedAt,
                IsOriginalVideoAvailable = video.FileUrl != string.Empty,
                IsReportAvailable = video.AiResults.Any(result => result.CreatedAt > reportCutoff),
                LatestJobId = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .ThenByDescending(job => job.Id)
                    .Select(job => (long?)job.Id)
                    .FirstOrDefault(),
                LatestJobStatus = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .ThenByDescending(job => job.Id)
                    .Select(job => job.Status.ToString())
                    .FirstOrDefault(),
                LatestJobProgress = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .ThenByDescending(job => job.Id)
                    .Select(job => (int?)job.Progress)
                    .FirstOrDefault(),
                LatestJobUpdatedAt = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .ThenByDescending(job => job.Id)
                    .Select(job => (DateTimeOffset?)job.LastActivityAt ?? job.UpdatedAt)
                    .FirstOrDefault(),
                CanRetry = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .ThenByDescending(job => job.Id)
                    .Select(job => job.Status == JobStatus.Failed && job.RetryCount < job.MaxRetryCount)
                    .FirstOrDefault(),
                CurrentStep = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .ThenByDescending(job => job.Id)
                    .Select(job => job.CurrentStep)
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResponse<VideoHistoryItemDto>>.SuccessResponse(new PagedResponse<VideoHistoryItemDto>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    public async Task<ApiResponse<VideoDetailDto>> GetVideoDetailAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .AsNoTracking()
            .Include(existingVideo => existingVideo.AnalysisJobs)
            .Where(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(cancellationToken);

        return video is null
            ? ApiResponse<VideoDetailDto>.ErrorResponse("Video was not found.")
            : ApiResponse<VideoDetailDto>.SuccessResponse(new VideoDetailDto
            {
                VideoId = video.Id,
                OriginalName = video.OriginalName,
                ContentType = video.ContentType,
                FileExtension = video.FileExtension,
                FileSize = video.FileSize,
                DurationSeconds = video.DurationSeconds,
                Status = video.Status.ToString(),
                CreatedAt = video.CreatedAt,
                UpdatedAt = video.UpdatedAt,
                LatestJob = video.AnalysisJobs
                    .OrderByDescending(job => job.CreatedAt)
                    .Select(MapJob)
                .FirstOrDefault()
            });
    }

    public async Task<ApiResponse<UploadVideoResponse>> ReanalyzeAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .FirstOrDefaultAsync(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted,
                cancellationToken);

        if (video is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Video was not found.");
        }

        if (!HasStoredMedia(video))
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("The original video file is no longer available for reanalysis.");
        }

        if (ExceedsCurrentAbsoluteUploadLimit(video))
        {
            return BuildCurrentSizeLimitError();
        }

        var activeJob = await dbContext.AnalysisJobs
            .Where(job => job.VideoId == video.Id)
            .Where(job => job.Status == JobStatus.Queued
                || job.Status == JobStatus.Preparing
                || job.Status == JobStatus.Processing
                || job.Status == JobStatus.Retrying
                || job.Status == JobStatus.Finalizing
                || job.Status == JobStatus.PauseRequested
                || job.Status == JobStatus.Paused
                || job.Status == JobStatus.ResumeRequested
                || job.Status == JobStatus.CancelRequested)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (activeJob is not null)
        {
            return ApiResponse<UploadVideoResponse>.SuccessResponse(
                ToUploadResponse(video, activeJob, "Analysis is already queued or processing."),
                "Analysis is already queued or processing.");
        }

        var analysisMode = await GetLatestAnalysisModeAsync(video.Id, AnalysisMode.Basic, cancellationToken);
        var clientContextResult = await TryResolveClientContextAsync(currentUserId, cancellationToken);
        if (clientContextResult.ErrorResponse is not null)
        {
            return clientContextResult.ErrorResponse;
        }

        var reservation = await entitlementService.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = currentUserId,
            AnalysisMode = analysisMode,
            FileSizeBytes = video.FileSize,
            ClientContext = clientContextResult.Context
        }, cancellationToken);
        if (!reservation.Success)
        {
            return ToUploadErrorResponse(reservation);
        }

        var job = new AnalysisJob
        {
            VideoId = video.Id,
            Status = JobStatus.Queued,
            Progress = 0,
            CurrentStep = "Waiting for reanalysis worker",
            RetryCount = 0,
            MaxRetryCount = _processingOptions.UserRetryLimit,
            ScanMode = analysisMode.ToString(),
            LastActivityAt = DateTimeOffset.UtcNow
        };

        var previousVideoStatus = video.Status;
        try
        {
            video.Status = VideoStatus.Queued;
            dbContext.AnalysisJobs.Add(job);
            await dbContext.SaveChangesAsync(cancellationToken);
            await LinkReservationToJobAsync(reservation.ReservationId, video.Id, job.Id, cancellationToken);
        }
        catch
        {
            if (reservation.ReservationId is not null)
            {
                await entitlementService.ReleaseReservationAsync(
                    reservation.ReservationId.Value,
                    "Reanalysis failed before it could be queued.",
                    CancellationToken.None);
            }

            throw;
        }

        string backgroundJobId;
        try
        {
            backgroundJobId = analysisJobQueue.EnqueueAnalysisJob(job.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to enqueue reanalysis job {AnalysisJobId} for video {VideoId}.", job.Id, video.Id);
            await MarkQueueFailureAsync(
                video,
                job,
                reservation.ReservationId,
                "Reanalysis failed before it could be queued.",
                deleteUploadedObjectKey: null,
                previousVideoStatus: previousVideoStatus);

            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Analysis queue is temporarily unavailable. Please try again shortly.",
                errorCode: VideoInfrastructureErrorCodes.AnalysisQueueUnavailable);
        }

        logger.LogInformation(
            "Queued reanalysis job {AnalysisJobId} as Hangfire job {BackgroundJobId} for video {VideoId}.",
            job.Id,
            backgroundJobId,
            video.Id);

        return ApiResponse<UploadVideoResponse>.SuccessResponse(
            ToUploadResponse(video, job, "Video queued for reanalysis."),
            "Video queued for reanalysis.");
    }

    public async Task<ApiResponse<UploadVideoResponse>> RetryAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .Include(existingVideo => existingVideo.AnalysisJobs)
            .FirstOrDefaultAsync(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted,
                cancellationToken);

        if (video is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Video was not found.");
        }

        if (!HasStoredMedia(video))
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("The original video file is no longer available for retry.");
        }

        if (ExceedsCurrentAbsoluteUploadLimit(video))
        {
            return BuildCurrentSizeLimitError();
        }

        var latestJob = video.AnalysisJobs
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .FirstOrDefault();

        var activeJob = video.AnalysisJobs
            .Where(IsActiveJob)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .FirstOrDefault();
        if (activeJob is not null)
        {
            return ApiResponse<UploadVideoResponse>.SuccessResponse(
                ToUploadResponse(video, activeJob, "Analysis retry is already queued or processing."),
                "Analysis retry is already queued or processing.");
        }

        if (latestJob is null)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("No failed analysis job was found for this video.");
        }

        if (latestJob.Status != JobStatus.Failed)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Only failed analyses can be retried from this endpoint.");
        }

        if (latestJob.RetryCount >= latestJob.MaxRetryCount)
        {
            return ApiResponse<UploadVideoResponse>.ErrorResponse("Maximum retry count was reached.");
        }

        var analysisMode = ParseAnalysisMode(latestJob.ScanMode, AnalysisMode.Basic);
        var clientContextResult = await TryResolveClientContextAsync(currentUserId, cancellationToken);
        if (clientContextResult.ErrorResponse is not null)
        {
            return clientContextResult.ErrorResponse;
        }

        var reservation = await entitlementService.ReserveScanAsync(new ScanReservationRequest
        {
            UserId = currentUserId,
            AnalysisMode = analysisMode,
            FileSizeBytes = video.FileSize,
            ClientContext = clientContextResult.Context
        }, cancellationToken);
        if (!reservation.Success)
        {
            return ToUploadErrorResponse(reservation);
        }

        var job = new AnalysisJob
        {
            VideoId = video.Id,
            Status = JobStatus.Queued,
            Progress = 0,
            CurrentStep = "Waiting for retry worker",
            RetryCount = latestJob.RetryCount + 1,
            MaxRetryCount = latestJob.MaxRetryCount,
            ScanMode = latestJob.ScanMode,
            PauseRequested = false,
            PauseRequestedAt = null,
            PausedAt = null,
            ResumedAt = null,
            PausedFromStage = null,
            LastCheckpoint = null,
            ResumeBackgroundJobId = null,
            LastActivityAt = DateTimeOffset.UtcNow
        };

        video.Status = VideoStatus.Queued;
        dbContext.AnalysisJobs.Add(job);
        try
        {
            await using var transaction = dbContext.Database.IsRelational()
                ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await dbContext.SaveChangesAsync(cancellationToken);
            await LinkReservationToJobAsync(reservation.ReservationId, video.Id, job.Id, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            if (reservation.ReservationId is not null)
            {
                await entitlementService.ReleaseReservationAsync(
                    reservation.ReservationId.Value,
                    "Retry failed before it could be queued.",
                    CancellationToken.None);
            }

            throw;
        }

        string backgroundJobId;
        try
        {
            backgroundJobId = analysisJobQueue.EnqueueAnalysisJob(job.Id);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to enqueue retry analysis job {AnalysisJobId} for video {VideoId}.", job.Id, video.Id);
            await MarkQueueFailureAsync(
                video,
                job,
                reservation.ReservationId,
                "Retry failed before it could be queued.",
                deleteUploadedObjectKey: null,
                previousVideoStatus: VideoStatus.Failed);

            return ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Analysis queue is temporarily unavailable. Please try again shortly.",
                errorCode: VideoInfrastructureErrorCodes.AnalysisQueueUnavailable);
        }

        logger.LogInformation(
            "Queued retry analysis job {AnalysisJobId} as Hangfire job {BackgroundJobId} for video {VideoId} from failed job {FailedJobId}.",
            job.Id,
            backgroundJobId,
            video.Id,
            latestJob.Id);

        return ApiResponse<UploadVideoResponse>.SuccessResponse(
            ToUploadResponse(video, job, "Analysis retry queued."),
            "Analysis retry queued.");
    }

    public async Task<ApiResponse<JobStatusDto>> PauseAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var job = await LoadLatestJobForUserAsync(videoId, currentUserId, cancellationToken);
        if (job is null)
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("Analysis job was not found.");
        }

        var now = DateTimeOffset.UtcNow;
        switch (job.Status)
        {
            case JobStatus.Queued:
            case JobStatus.Preparing:
            case JobStatus.Processing:
            case JobStatus.Retrying:
            case JobStatus.Finalizing:
            case JobStatus.ResumeRequested:
                job.PauseRequested = true;
                job.PauseRequestedAt ??= now;
                job.Status = JobStatus.PauseRequested;
                job.CurrentStep = "Pausing analysis";
                job.LastActivityAt = now;
                job.ResumeBackgroundJobId = null;
                await dbContext.SaveChangesAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                logger.LogInformation(
                    "Pause requested for analysis job {JobId} video {VideoId} by user {UserId}.",
                    job.Id,
                    videoId,
                    currentUserId);
                await jobLogService.LogAsync(job.Id, "PauseRequested", "Information", "Analysis pause was requested by the user.", null, cancellationToken);
                return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Pause requested.");

            case JobStatus.PauseRequested:
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                logger.LogInformation("Duplicate pause request ignored for analysis job {JobId} video {VideoId}.", job.Id, videoId);
                return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Pause is already requested.");

            case JobStatus.Paused:
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "The analysis is already paused.");

            case JobStatus.Completed:
                logger.LogWarning("Invalid pause transition for completed job {JobId} video {VideoId}.", job.Id, videoId);
                return ApiResponse<JobStatusDto>.ErrorResponse("The analysis has already completed.");

            case JobStatus.Failed:
            case JobStatus.Cancelled:
            case JobStatus.CancelRequested:
            default:
                logger.LogWarning("Invalid pause transition for job {JobId} video {VideoId} status {Status}.", job.Id, videoId, job.Status);
                return ApiResponse<JobStatusDto>.ErrorResponse("This analysis cannot be paused in its current state.");
        }
    }

    public async Task<ApiResponse<JobStatusDto>> ResumeAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        var job = await LoadLatestJobForUserAsync(videoId, currentUserId, cancellationToken);
        if (job is null)
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("Analysis job was not found.");
        }

        if (job.Video is null || !HasStoredMedia(job.Video))
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("The original video file is no longer available for resume.");
        }

        if (job.Status == JobStatus.Cancelled || job.Status == JobStatus.CancelRequested || job.CancelRequested)
        {
            logger.LogWarning("Resume rejected for cancelled job {JobId} video {VideoId}.", job.Id, videoId);
            return ApiResponse<JobStatusDto>.ErrorResponse("The analysis was cancelled before it could be resumed.");
        }

        if (job.Status is JobStatus.Completed)
        {
            logger.LogWarning("Resume rejected for completed job {JobId} video {VideoId}.", job.Id, videoId);
            return ApiResponse<JobStatusDto>.ErrorResponse("The analysis has already completed.");
        }

        if (job.Status is JobStatus.Failed)
        {
            logger.LogWarning("Resume rejected for failed job {JobId} video {VideoId}.", job.Id, videoId);
            return ApiResponse<JobStatusDto>.ErrorResponse("This analysis cannot be resumed in its current state.");
        }

        if (job.Status == JobStatus.ResumeRequested)
        {
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
            logger.LogInformation("Duplicate resume request prevented for active job {JobId} video {VideoId} status {Status}.", job.Id, videoId, job.Status);
            return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "The analysis is already queued or processing.");
        }

        if (job.Status is JobStatus.Queued or JobStatus.Preparing or JobStatus.Processing or JobStatus.Retrying or JobStatus.Finalizing)
        {
            logger.LogWarning("Invalid resume transition for active job {JobId} video {VideoId} status {Status}.", job.Id, videoId, job.Status);
            return ApiResponse<JobStatusDto>.ErrorResponse("This analysis cannot be resumed in its current state.");
        }

        if (job.Status != JobStatus.Paused)
        {
            logger.LogWarning("Invalid resume transition for job {JobId} video {VideoId} status {Status}.", job.Id, videoId, job.Status);
            return ApiResponse<JobStatusDto>.ErrorResponse("This analysis cannot be resumed in its current state.");
        }

        var now = DateTimeOffset.UtcNow;
        if (dbContext.Database.IsRelational())
        {
            var updatedRows = await dbContext.AnalysisJobs
                .Where(existingJob => existingJob.Id == job.Id
                    && existingJob.Status == JobStatus.Paused
                    && existingJob.ResumeBackgroundJobId == null)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(existingJob => existingJob.Status, JobStatus.ResumeRequested)
                    .SetProperty(existingJob => existingJob.PauseRequested, false)
                    .SetProperty(existingJob => existingJob.PauseRequestedAt, (DateTimeOffset?)null)
                    .SetProperty(existingJob => existingJob.ResumedAt, now)
                    .SetProperty(existingJob => existingJob.CurrentStep, "Resuming analysis")
                    .SetProperty(existingJob => existingJob.LastActivityAt, now)
                    .SetProperty(existingJob => existingJob.UpdatedAt, now),
                    cancellationToken);
            if (updatedRows == 0)
            {
                await dbContext.Entry(job).ReloadAsync(cancellationToken);
                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }
                logger.LogInformation("Duplicate resume request prevented for job {JobId} video {VideoId}.", job.Id, videoId);
                return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "The analysis is already queued or processing.");
            }

            await dbContext.Videos
                .Where(video => video.Id == videoId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(video => video.Status, VideoStatus.Queued), cancellationToken);
        }

        job.Status = JobStatus.ResumeRequested;
        job.PauseRequested = false;
        job.PauseRequestedAt = null;
        job.ResumedAt = now;
        job.ResumeBackgroundJobId = null;
        job.CurrentStep = "Resuming analysis";
        job.LastActivityAt = now;
        if (job.Video is not null)
        {
            job.Video.Status = VideoStatus.Queued;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var backgroundJobId = analysisJobQueue.ResumeAnalysisJob(job.Id);
        job.ResumeBackgroundJobId = backgroundJobId;
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        logger.LogInformation(
            "Resume requested for analysis job {JobId} video {VideoId} by user {UserId}; enqueued Hangfire job {BackgroundJobId}.",
            job.Id,
            videoId,
            currentUserId,
            backgroundJobId);
        await jobLogService.LogAsync(job.Id, "ResumeRequested", "Information", "Analysis resume was requested by the user.", new { backgroundJobId }, cancellationToken);
        return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Analysis resume queued.");
    }

    public async Task<ApiResponse<JobStatusDto>> CancelAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.AnalysisJobs
            .Include(existingJob => existingJob.Video)
            .Include(existingJob => existingJob.Segments)
            .Where(existingJob => existingJob.VideoId == videoId
                && existingJob.Video.UserId == currentUserId
                && existingJob.Video.DeletedAt == null
                && existingJob.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(existingJob => existingJob.CreatedAt)
            .ThenByDescending(existingJob => existingJob.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("Analysis job was not found.");
        }

        if (job.Status == JobStatus.Completed)
        {
            return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Analysis already completed.");
        }

        if (job.Status is JobStatus.Failed or JobStatus.Cancelled)
        {
            return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Analysis is not running.");
        }

        var now = DateTimeOffset.UtcNow;
        if (job.Status == JobStatus.Paused)
        {
            job.CancelRequested = true;
            job.CancelRequestedAt ??= now;
            job.PauseRequested = false;
            job.PauseRequestedAt = null;
            job.ResumeBackgroundJobId = null;
            job.Status = JobStatus.Cancelled;
            job.CurrentStep = "Analysis cancelled";
            job.CompletedAt = now;
            job.LastActivityAt = now;
            if (job.Video is not null)
            {
                job.Video.Status = VideoStatus.Cancelled;
            }

            foreach (var segment in job.Segments.Where(segment => segment.Status != AnalysisSegmentStatus.Completed))
            {
                segment.Status = AnalysisSegmentStatus.Cancelled;
                segment.LastActivityAt = now;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await ReleaseReservationForJobAsync(job.Id, "Paused analysis was cancelled by the user.", cancellationToken);
            await jobLogService.LogAsync(job.Id, "Cancelled", "Information", "Paused analysis was cancelled by the user.", null, cancellationToken);
            logger.LogInformation("Paused analysis job {JobId} video {VideoId} was cancelled by user {UserId}.", job.Id, videoId, currentUserId);
            return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Cancellation requested.");
        }

        job.CancelRequested = true;
        job.CancelRequestedAt ??= now;
        job.PauseRequested = false;
        job.PauseRequestedAt = null;
        job.ResumeBackgroundJobId = null;
        job.LastActivityAt = now;
        job.Status = JobStatus.CancelRequested;
        job.CurrentStep = "Cancelling analysis";
        foreach (var segment in job.Segments.Where(segment => segment.Status is AnalysisSegmentStatus.Pending or AnalysisSegmentStatus.Preparing or AnalysisSegmentStatus.Ready or AnalysisSegmentStatus.Analyzing))
        {
            segment.Status = AnalysisSegmentStatus.CancelRequested;
            segment.LastActivityAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await jobLogService.LogAsync(job.Id, "CancellationRequested", "Information", "Analysis cancellation was requested by the user.", null, cancellationToken);

        return ApiResponse<JobStatusDto>.SuccessResponse(MapJob(job), "Cancellation requested.");
    }

    private async Task<AnalysisJob?> LoadLatestJobForUserAsync(long videoId, long currentUserId, CancellationToken cancellationToken)
    {
        return await dbContext.AnalysisJobs
            .Include(existingJob => existingJob.Video)
            .Include(existingJob => existingJob.Segments)
            .Where(existingJob => existingJob.VideoId == videoId
                && existingJob.Video.UserId == currentUserId
                && existingJob.Video.DeletedAt == null
                && existingJob.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(existingJob => existingJob.CreatedAt)
            .ThenByDescending(existingJob => existingJob.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<ApiResponse<MetadataResultDto>> GetMetadataAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await dbContext.MetadataResults
            .AsNoTracking()
            .Include(result => result.Video)
            .Where(result => result.VideoId == videoId
                && result.Video.UserId == currentUserId
                && result.Video.DeletedAt == null
                && result.Video.Status != VideoStatus.Deleted)
            .FirstOrDefaultAsync(cancellationToken);

        return metadata is null
            ? ApiResponse<MetadataResultDto>.ErrorResponse("Video metadata was not found.")
            : ApiResponse<MetadataResultDto>.SuccessResponse(new MetadataResultDto
            {
                VideoId = metadata.VideoId,
                Codec = metadata.Codec,
                AudioCodec = metadata.AudioCodec,
                Fps = metadata.Fps,
                Resolution = metadata.Resolution,
                DurationSeconds = metadata.DurationSeconds,
                Bitrate = metadata.Bitrate,
                Encoder = metadata.Encoder,
                CreationTime = metadata.CreationTime,
                HasMissingMetadata = metadata.HasMissingMetadata,
                WarningsJson = metadata.WarningsJson,
                CreatedAt = metadata.CreatedAt
            });
    }

    public async Task<ApiResponse<IReadOnlyList<VideoFrameDto>>> GetFramesAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var ownsVideo = await dbContext.Videos
            .AsNoTracking()
            .AnyAsync(video => video.Id == videoId
                && video.UserId == currentUserId
                && video.DeletedAt == null
                && video.Status != VideoStatus.Deleted,
                cancellationToken);

        if (!ownsVideo)
        {
            return ApiResponse<IReadOnlyList<VideoFrameDto>>.ErrorResponse("Video frames were not found.");
        }

        var frames = await dbContext.VideoFrames
            .AsNoTracking()
            .Where(frame => frame.VideoId == videoId && frame.FrameUrl != string.Empty)
            .OrderBy(frame => frame.FrameIndex)
            .Select(frame => new VideoFrameDto
            {
                Id = frame.Id,
                VideoId = frame.VideoId,
                FrameUrl = frame.FrameUrl,
                TimestampSeconds = frame.TimestampSeconds,
                FrameIndex = frame.FrameIndex,
                Width = frame.Width,
                Height = frame.Height,
                IsKeyframe = frame.IsKeyframe,
                CreatedAt = frame.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<IReadOnlyList<VideoFrameDto>>.SuccessResponse(frames);
    }

    public async Task<ApiResponse<AnalysisResultDto>> GetAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await dbContext.AiResults
            .AsNoTracking()
            .Include(aiResult => aiResult.Video)
            .Include(aiResult => aiResult.ModelVersion)
            .Include(aiResult => aiResult.EvidenceItems)
            .Where(aiResult => aiResult.VideoId == videoId
                && aiResult.Video.UserId == currentUserId
                && aiResult.Video.DeletedAt == null
                && aiResult.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(aiResult => aiResult.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return ApiResponse<AnalysisResultDto>.ErrorResponse("Analysis result is not available yet.");
        }

        var scanMode = await dbContext.AnalysisJobs
            .AsNoTracking()
            .Where(job => job.VideoId == videoId)
            .OrderByDescending(job => job.CreatedAt)
            .Select(job => job.ScanMode)
            .FirstOrDefaultAsync(cancellationToken);

        return ApiResponse<AnalysisResultDto>.SuccessResponse(new AnalysisResultDto
            {
                VideoId = result.VideoId,
                AiResultId = result.Id,
                ModelId = SanitizeProviderValue(GetRawString(result.RawModelOutputJson, "model_id") ?? result.ModelVersion?.Version),
                ModelVersion = SanitizeProviderValue(result.ModelVersion?.Version),
                ModelCapability = GetRawString(result.RawModelOutputJson, "model_capability"),
                IsMock = GetRawBool(result.RawModelOutputJson, "is_mock"),
                AiGeneratedProbability = ToPercentage(result.FinalScore),
                LikelyRealProbability = ToPercentage(1m - result.FinalScore),
                ConfidencePercentage = ToPercentage(result.Confidence),
                VisualScore = result.VisualScore,
                MetadataScore = result.MetadataScore,
                TemporalScore = result.TemporalScore,
                FinalScore = result.FinalScore,
                Confidence = result.Confidence,
                Label = result.Label.ToString(),
                Summary = SanitizeProviderText(result.Summary),
                Warnings = GetRawStringArray(result.RawModelOutputJson, "warnings"),
                Provider = SanitizeProviderValue(result.Provider) ?? "Internal",
                ProviderMode = SanitizeProviderValue(result.ProviderMode) ?? "internal",
                ScanMode = SanitizeProviderValue(scanMode),
                FinalDecisionSource = SanitizeProviderValue(result.FinalDecisionSource) ?? "Internal",
                ExternalProviderName = SanitizeProviderValue(result.ExternalProviderName),
                ExternalProviderStatus = result.ExternalProviderStatus,
                ExternalScore = result.ExternalScore,
                ExternalConfidence = result.ExternalConfidence,
                ExternalLabel = result.ExternalLabel,
                FallbackUsed = result.FallbackUsed,
                FallbackReason = SanitizeProviderText(result.FallbackReason),
                ProviderWarnings = GetRawStringArray(result.RawModelOutputJson, "warnings"),
                LocalAnalysisSummary = BuildProviderSummary("Local", result.LocalResultJson),
                ExternalAnalysisSummary = BuildExternalSummary(result),
                HybridDecisionSummary = result.ProviderMode.Equals("hybrid", StringComparison.OrdinalIgnoreCase)
                    ? SanitizeProviderText(result.Summary)
                    : null,
                ProviderRequestedAt = result.ExternalRequestedAt,
                ProviderCompletedAt = result.ExternalCompletedAt,
                ModelDisagreement = GetRawBool(result.RawModelOutputJson, "model_disagreement"),
                StrongFrameEvidence = GetRawBool(result.RawModelOutputJson, "strong_frame_evidence"),
                MinimumRecommendedScore = GetRawDecimal(result.RawModelOutputJson, "minimum_recommended_score"),
                EnsembleStrategy = GetRawString(result.RawModelOutputJson, "ensemble_strategy"),
                ComponentScoresJson = SanitizeProviderText(GetRawJson(result.RawModelOutputJson, "component_scores")),
                CreatedAt = result.CreatedAt,
                EvidenceItems = result.EvidenceItems
                    .OrderByDescending(item => item.Severity)
                    .ThenBy(item => item.Id)
                    .Select(MapEvidence)
                    .ToList()
            });
    }

    public async Task<ApiResponse<IReadOnlyList<EvidenceItemDto>>> GetEvidenceAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var result = await dbContext.AiResults
            .AsNoTracking()
            .Include(aiResult => aiResult.Video)
            .Include(aiResult => aiResult.EvidenceItems)
            .Where(aiResult => aiResult.VideoId == videoId
                && aiResult.Video.UserId == currentUserId
                && aiResult.Video.DeletedAt == null
                && aiResult.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(aiResult => aiResult.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return result is null
            ? ApiResponse<IReadOnlyList<EvidenceItemDto>>.ErrorResponse("Analysis result is not available yet.")
            : ApiResponse<IReadOnlyList<EvidenceItemDto>>.SuccessResponse(result.EvidenceItems
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Id)
                .Select(MapEvidence)
                .ToList());
    }

    public async Task<ApiResponse<bool>> DeleteVideoAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .FirstOrDefaultAsync(existingVideo => existingVideo.Id == videoId
                && existingVideo.UserId == currentUserId
                && existingVideo.DeletedAt == null
                && existingVideo.Status != VideoStatus.Deleted,
                cancellationToken);

        if (video is null)
        {
            return ApiResponse<bool>.ErrorResponse("Video was not found.");
        }

        video.Status = VideoStatus.Deleted;
        video.DeletedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return ApiResponse<bool>.SuccessResponse(true, "Video deleted successfully.");
    }

    internal static JobStatusDto MapJob(AnalysisJob job)
    {
        var errorCategory = MapErrorCategory(job.ErrorCode, job.ErrorMessage);
        var userMessage = job.Status == JobStatus.Failed
            ? GetUserMessage(errorCategory)
            : null;

        return new JobStatusDto
        {
            JobId = job.Id,
            VideoId = job.VideoId,
            OriginalName = job.Video?.OriginalName ?? string.Empty,
            Status = job.Status.ToString(),
            Progress = job.Progress,
            CurrentStep = job.CurrentStep,
            LastCheckpoint = job.LastCheckpoint,
            ScanMode = job.ScanMode,
            CompletedSegments = job.CompletedSegments,
            TotalSegments = job.TotalSegments,
            AnalyzedCoverageSeconds = job.AnalyzedCoverageSeconds,
            TotalDurationSeconds = job.TotalDurationSeconds,
            LastActivityAt = job.LastActivityAt,
            ErrorMessage = userMessage,
            ErrorCode = job.Status == JobStatus.Failed ? errorCategory : null,
            UserMessage = userMessage,
            CanRetry = job.Status == JobStatus.Failed && job.RetryCount < job.MaxRetryCount,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            FailedStage = job.Status == JobStatus.Failed ? GetFailedStage(job.CurrentStep) : null,
            NextRecommendedAction = job.Status == JobStatus.Failed
                ? job.RetryCount < job.MaxRetryCount
                    ? "RetryAnalysis"
                    : "UploadAnotherVideo"
                : null,
            TechnicalReferenceId = job.Status == JobStatus.Failed ? $"JOB-{job.Id}" : null,
            CreatedAt = job.CreatedAt,
            StartedAt = job.StartedAt,
            CompletedAt = job.CompletedAt,
            LastUpdatedAt = job.UpdatedAt
        };
    }

    private static UploadVideoResponse ToUploadResponse(Video video, AnalysisJob job, string message)
    {
        return new UploadVideoResponse
        {
            VideoId = video.Id,
            JobId = job.Id,
            Status = video.Status.ToString(),
            JobStatus = job.Status.ToString(),
            OriginalName = video.OriginalName,
            FileSize = video.FileSize,
            ContentType = video.ContentType ?? string.Empty,
            RetryCount = job.RetryCount,
            MaxRetryCount = job.MaxRetryCount,
            Message = message
        };
    }

    private static UploadVideoResponse ToGuestUploadResponse(UploadVideoResponse response, string guestAccessToken)
    {
        return new UploadVideoResponse
        {
            VideoId = response.VideoId,
            JobId = response.JobId,
            Status = response.Status,
            JobStatus = response.JobStatus,
            OriginalName = response.OriginalName,
            FileSize = response.FileSize,
            ContentType = response.ContentType,
            RetryCount = response.RetryCount,
            MaxRetryCount = response.MaxRetryCount,
            GuestAccessToken = guestAccessToken,
            Message = response.Message
        };
    }

    private static bool HasStoredMedia(Video video)
    {
        return !string.IsNullOrWhiteSpace(video.FileUrl);
    }

    private bool ExceedsCurrentAbsoluteUploadLimit(Video video)
    {
        return video.FileSize > _uploadOptions.MaxFileSizeBytes;
    }

    private static ApiResponse<UploadVideoResponse> BuildCurrentSizeLimitError()
    {
        return ApiResponse<UploadVideoResponse>.ErrorResponse(
            "Videos larger than 300 MB are not supported at this time.",
            errorCode: SubscriptionErrorCodes.VideoSizeLimitExceeded);
    }

    private async Task<(SubscriptionClientContext? Context, ApiResponse<UploadVideoResponse>? ErrorResponse)> TryResolveClientContextAsync(
        long currentUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await deviceIdentityService.ResolveAsync(currentUserId, cancellationToken), null);
        }
        catch (InvalidOperationException exception)
            when (exception.Message == SubscriptionErrorCodes.SubscriptionSecurityNotConfigured)
        {
            return (null, ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Subscription security is not configured.",
                errorCode: SubscriptionErrorCodes.SubscriptionSecurityNotConfigured));
        }
    }

    private async Task<(SubscriptionClientContext? Context, ApiResponse<UploadVideoResponse>? ErrorResponse)> TryResolveAnonymousClientContextAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            return (await deviceIdentityService.ResolveAnonymousAsync(cancellationToken), null);
        }
        catch (InvalidOperationException exception)
            when (exception.Message == SubscriptionErrorCodes.SubscriptionSecurityNotConfigured)
        {
            return (null, ApiResponse<UploadVideoResponse>.ErrorResponse(
                "Subscription security is not configured.",
                errorCode: SubscriptionErrorCodes.SubscriptionSecurityNotConfigured));
        }
    }

    private async Task LinkReservationToJobAsync(
        long? reservationId,
        long videoId,
        long jobId,
        CancellationToken cancellationToken)
    {
        if (reservationId is null)
        {
            return;
        }

        var reservation = await dbContext.ScanReservations
            .FirstOrDefaultAsync(candidate => candidate.Id == reservationId.Value, cancellationToken);
        if (reservation is null)
        {
            return;
        }

        reservation.VideoId = videoId;
        reservation.AnalysisJobId = jobId;
        reservation.ExpiresAt = null;
        reservation.LastHeartbeatAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<AnalysisMode> GetLatestAnalysisModeAsync(
        long videoId,
        AnalysisMode fallback,
        CancellationToken cancellationToken)
    {
        var latestScanMode = await dbContext.AnalysisJobs
            .Where(job => job.VideoId == videoId)
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .Select(job => job.ScanMode)
            .FirstOrDefaultAsync(cancellationToken);

        return ParseAnalysisMode(latestScanMode, fallback);
    }

    private static AnalysisMode ParseAnalysisMode(string? scanMode, AnalysisMode fallback)
    {
        if (string.IsNullOrWhiteSpace(scanMode))
        {
            return fallback;
        }

        return scanMode.Contains("Detailed", StringComparison.OrdinalIgnoreCase)
            ? AnalysisMode.Detailed
            : scanMode.Contains("Smart", StringComparison.OrdinalIgnoreCase) || scanMode.Contains("Basic", StringComparison.OrdinalIgnoreCase)
                ? AnalysisMode.Basic
                : fallback;
    }

    private static ApiResponse<UploadVideoResponse> ToUploadErrorResponse(ScanReservationResult reservation)
    {
        return ApiResponse<UploadVideoResponse>.ErrorResponse(
            reservation.Message,
            errorCode: reservation.ErrorCode);
    }

    private async Task ReleaseReservationForJobAsync(
        long jobId,
        string reason,
        CancellationToken cancellationToken)
    {
        var reservationId = await dbContext.ScanReservations
            .Where(reservation =>
                reservation.AnalysisJobId == jobId &&
                reservation.Status == Domain.Constants.ScanReservationStatuses.Reserved)
            .Select(reservation => (long?)reservation.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (reservationId is null)
        {
            return;
        }

        await entitlementService.ReleaseReservationAsync(reservationId.Value, reason, cancellationToken);
    }

    private async Task MarkQueueFailureAsync(
        Video video,
        AnalysisJob job,
        long? reservationId,
        string reservationReleaseReason,
        string? deleteUploadedObjectKey,
        VideoStatus previousVideoStatus)
    {
        if (reservationId is not null)
        {
            await entitlementService.ReleaseReservationAsync(
                reservationId.Value,
                reservationReleaseReason,
                CancellationToken.None);
        }

        if (!string.IsNullOrWhiteSpace(deleteUploadedObjectKey))
        {
            try
            {
                await objectStorageService.DeleteAsync(deleteUploadedObjectKey, CancellationToken.None);
                video.FileUrl = string.Empty;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to clean uploaded object after queue failure for video {VideoId}.", video.Id);
            }
        }

        job.Status = JobStatus.Failed;
        job.Progress = 0;
        job.CurrentStep = "Failed: Analysis queue is temporarily unavailable. Please try again shortly.";
        job.ErrorCode = VideoInfrastructureErrorCodes.AnalysisQueueUnavailable;
        job.ErrorMessage = "Analysis queue is temporarily unavailable. Please try again shortly.";
        job.FailedStage = "QueueAnalysis";
        job.FailedAt = DateTimeOffset.UtcNow;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.LastActivityAt = DateTimeOffset.UtcNow;
        video.Status = previousVideoStatus;
        video.UpdatedAt = DateTimeOffset.UtcNow;

        try
        {
            await dbContext.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to persist queue failure compensation for analysis job {AnalysisJobId}.", job.Id);
        }
    }

    private static bool IsActiveJob(AnalysisJob job)
    {
        return job.Status is JobStatus.Queued
            or JobStatus.Preparing
            or JobStatus.Processing
            or JobStatus.Retrying
            or JobStatus.Finalizing
            or JobStatus.PauseRequested
            or JobStatus.Paused
            or JobStatus.ResumeRequested
            or JobStatus.CancelRequested;
    }

    private static string MapErrorCategory(string? errorCode, string? errorMessage)
    {
        var normalizedCode = (errorCode ?? string.Empty).Trim().ToUpperInvariant();
        var normalizedMessage = (errorMessage ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedCode.Contains("AUTH") || normalizedMessage.Contains("HTTP 401") || normalizedMessage.Contains("UNAUTHORIZED"))
        {
            return "ProviderAuthenticationFailed";
        }

        if (normalizedCode.Contains("RATE") || normalizedMessage.Contains("HTTP 429") || normalizedMessage.Contains("RATE LIMIT"))
        {
            return "ProviderRateLimited";
        }

        if (normalizedCode.Contains("TIMEOUT") || normalizedMessage.Contains("TIMED OUT"))
        {
            return "ProviderTimeout";
        }

        if (normalizedCode.Contains("BITMIND") || normalizedCode.Contains("PROVIDER") || normalizedCode.Contains("AI_SERVICE_UNAVAILABLE") || normalizedCode.Contains("AI_VIDEO_SERVICE_UNAVAILABLE"))
        {
            return "ProviderUnavailable";
        }

        if (normalizedCode.Contains("TOO_LARGE") || normalizedMessage.Contains("UPLOAD LIMIT"))
        {
            return "VideoTooLarge";
        }

        if (normalizedCode.Contains("COMPRESSION"))
        {
            return "CompressionFailed";
        }

        if (normalizedCode.Contains("INVALID") || normalizedCode.Contains("FFPROBE") || normalizedCode.Contains("METADATA"))
        {
            return "InvalidVideo";
        }

        if (normalizedCode.Contains("NETWORK") || normalizedCode.Contains("HTTP_REQUEST"))
        {
            return "NetworkFailure";
        }

        if (normalizedCode.Contains("PROCESS") || normalizedCode.Contains("FFMPEG") || normalizedCode.Contains("AI_ANALYSIS"))
        {
            return "ProcessingFailed";
        }

        return "UnknownFailure";
    }

    private static string GetUserMessage(string errorCategory)
    {
        return errorCategory switch
        {
            "ProviderAuthenticationFailed" => "The external analysis service is temporarily unavailable. Please try again later.",
            "ProviderRateLimited" => "The analysis service is currently busy. Please wait a moment and retry.",
            "ProviderTimeout" => "The external analysis service took too long to respond. Please retry.",
            "ProviderUnavailable" => "External video analysis is temporarily unavailable.",
            "VideoTooLarge" => "The video could not be prepared for external analysis.",
            "CompressionFailed" => "We could not prepare this video for analysis. Please retry or upload a different format.",
            "InvalidVideo" => "This file could not be processed as a valid video.",
            "NetworkFailure" => "A temporary connection problem interrupted the analysis.",
            "ProcessingFailed" => "We could not complete the analysis. Please retry.",
            _ => "Something went wrong while processing this video."
        };
    }

    private static string? GetFailedStage(string? currentStep)
    {
        if (string.IsNullOrWhiteSpace(currentStep))
        {
            return null;
        }

        return currentStep.StartsWith("Failed:", StringComparison.OrdinalIgnoreCase)
            ? "Failed"
            : currentStep;
    }

    private static EvidenceItemDto MapEvidence(EvidenceItem item)
    {
        return new EvidenceItemDto
        {
            Id = item.Id,
            Type = item.Type.ToString(),
            Severity = item.Severity.ToString(),
            Title = SanitizeProviderText(item.Title) ?? item.Title,
            Description = SanitizeProviderText(item.Description) ?? item.Description,
            ScoreImpact = item.ScoreImpact,
            TimestampSeconds = item.TimestampSeconds,
            VideoFrameId = item.VideoFrameId
        };
    }

    private static decimal ToPercentage(decimal value)
    {
        return Math.Round(Math.Clamp(value, 0m, 1m) * 100m, 2);
    }

    private static string? GetRawString(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool GetRawBool(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                && value.GetBoolean();
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetRawStringArray(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            if (!document.RootElement.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => SanitizeProviderText(item.GetString()!)!)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static decimal? GetRawDecimal(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDecimal()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? GetRawJson(string? rawJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.GetRawText()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildProviderSummary(string providerName, string? resultJson)
    {
        if (string.IsNullOrWhiteSpace(resultJson))
        {
            return null;
        }

        var score = GetRawDecimal(resultJson, "overall_ai_score");
        var confidence = GetRawDecimal(resultJson, "overall_confidence");
        var label = GetRawString(resultJson, "label_hint");
        return $"{SanitizeProviderValue(providerName)}: {label ?? "analysis completed"}"
            + (score is null ? string.Empty : $" ({ToPercentage(score.Value)}% AI)")
            + (confidence is null ? string.Empty : $", {ToPercentage(confidence.Value)}% confidence");
    }

    private static string? BuildExternalSummary(AiResult result)
    {
        if (string.IsNullOrWhiteSpace(result.ExternalProviderName))
        {
            return null;
        }

        return $"{SanitizeProviderValue(result.ExternalProviderName)}: {result.ExternalProviderStatus ?? "Unknown"}"
            + (result.ExternalLabel is null ? string.Empty : $", {result.ExternalLabel}")
            + (result.ExternalScore is null ? string.Empty : $" ({ToPercentage(result.ExternalScore.Value)}% AI)")
            + (result.ExternalConfidence is null ? string.Empty : $", {ToPercentage(result.ExternalConfidence.Value)}% confidence");
    }

    private static string? SanitizeProviderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Trim().Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (normalized.Contains("bitmind", StringComparison.OrdinalIgnoreCase))
        {
            return "External";
        }

        if (string.Equals(normalized, "FallbackLocal", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "Local", StringComparison.OrdinalIgnoreCase))
        {
            return "Internal";
        }

        return SanitizeProviderText(value);
    }

    private static string? SanitizeProviderText(string? value)
    {
        return value?
            .Replace("bitmind-oracle-v1-sn34", "external-verification-model", StringComparison.OrdinalIgnoreCase)
            .Replace("bitmind-subnet-34", "external-verification-model", StringComparison.OrdinalIgnoreCase)
            .Replace("External BitMind verification", "External verification", StringComparison.OrdinalIgnoreCase)
            .Replace("BitMind", "external verification", StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateObjectKey(long userId, string extension)
    {
        var now = DateTimeOffset.UtcNow;
        return $"videos/{userId}/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}{extension}";
    }

    private static string SanitizeOriginalName(string originalName)
    {
        var fileName = Path.GetFileName(originalName);
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName.Length <= 500 ? fileName : fileName[..500];
    }

    private static bool HasSupportedVideoSignature(string filePath, string extension)
    {
        Span<byte> header = stackalloc byte[4096];
        using var stream = File.OpenRead(filePath);
        var bytesRead = stream.Read(header);
        var slice = header[..bytesRead];

        return extension switch
        {
            ".mp4" => HasIsoBaseMediaSignature(slice, allowQuickTimeWithoutFileTypeBox: false),
            ".mov" => HasIsoBaseMediaSignature(slice, allowQuickTimeWithoutFileTypeBox: true),
            ".mkv" or ".webm" => StartsWith(slice, [0x1A, 0x45, 0xDF, 0xA3]),
            ".avi" => HasAviSignature(slice),
            _ => false
        };
    }

    private async Task<bool> HasSupportedVideoContentAsync(
        string filePath,
        string extension,
        CancellationToken cancellationToken)
    {
        if (HasSupportedVideoSignature(filePath, extension))
        {
            return true;
        }

        try
        {
            var metadata = await metadataExtractionService.ExtractMetadataAsync(
                new VideoProcessingInput(0, 0, filePath, Path.GetTempPath(), null),
                cancellationToken);

            return !string.IsNullOrWhiteSpace(metadata.Codec);
        }
        catch (ProcessingException exception)
        {
            logger.LogInformation(
                exception,
                "Uploaded video signature fallback validation failed for extension {Extension}.",
                extension);
            return false;
        }
    }

    private static bool HasIsoBaseMediaSignature(ReadOnlySpan<byte> header, bool allowQuickTimeWithoutFileTypeBox)
    {
        var offset = 0;
        while (offset + 8 <= header.Length)
        {
            var size = ReadBigEndianUInt32(header.Slice(offset, 4));
            var boxType = header.Slice(offset + 4, 4);
            if (SequenceEqualsAscii(boxType, "ftyp"))
            {
                return true;
            }

            if (allowQuickTimeWithoutFileTypeBox && IsQuickTimeTopLevelBox(boxType))
            {
                return true;
            }

            if (size < 8)
            {
                return false;
            }

            if (size == 1)
            {
                return false;
            }

            if (size > int.MaxValue)
            {
                return false;
            }

            offset += (int)size;
        }

        return false;
    }

    private static bool HasAviSignature(ReadOnlySpan<byte> header)
    {
        return header.Length >= 12
            && header[0] == (byte)'R'
            && header[1] == (byte)'I'
            && header[2] == (byte)'F'
            && header[3] == (byte)'F'
            && header[8] == (byte)'A'
            && header[9] == (byte)'V'
            && header[10] == (byte)'I';
    }

    private static bool StartsWith(ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix)
    {
        return value.Length >= prefix.Length && value[..prefix.Length].SequenceEqual(prefix);
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> value)
    {
        return ((uint)value[0] << 24)
            | ((uint)value[1] << 16)
            | ((uint)value[2] << 8)
            | value[3];
    }

    private static bool SequenceEqualsAscii(ReadOnlySpan<byte> value, string expected)
    {
        if (value.Length != expected.Length)
        {
            return false;
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (value[index] != (byte)expected[index])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsQuickTimeTopLevelBox(ReadOnlySpan<byte> boxType)
    {
        return SequenceEqualsAscii(boxType, "moov")
            || SequenceEqualsAscii(boxType, "mdat")
            || SequenceEqualsAscii(boxType, "wide")
            || SequenceEqualsAscii(boxType, "free");
    }
}
