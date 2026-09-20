using System.Text.Json;
using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Admin.Interfaces;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Constants;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Videos.Ai;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Admin;

public sealed class AdminDashboardService(
    AppDbContext dbContext,
    IMemoryCache memoryCache,
    IProviderCircuitBreaker providerCircuitBreaker,
    IAuditLogService auditLogService,
    IOptions<VideoProcessingOptions> videoProcessingOptions) : IAdminDashboardService
{
    private const string BitMindProviderName = "BitMind";
    private const string SummaryCacheKey = "admin-dashboard-summary:v1";
    private const string ManualPlanPrefix = "ADMIN";
    private static readonly TimeSpan SummaryCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly VideoProcessingOptions _videoProcessingOptions = videoProcessingOptions.Value;
    private static readonly JobStatus[] PendingJobStatuses =
    [
        JobStatus.Queued,
        JobStatus.Preparing,
        JobStatus.Processing,
        JobStatus.Retrying,
        JobStatus.Finalizing,
        JobStatus.PauseRequested,
        JobStatus.ResumeRequested,
        JobStatus.CancelRequested
    ];

    public async Task<ApiResponse<AdminDashboardSummaryDto>> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (memoryCache.TryGetValue(SummaryCacheKey, out AdminDashboardSummaryDto? cachedSummary) && cachedSummary is not null)
        {
            return ApiResponse<AdminDashboardSummaryDto>.SuccessResponse(cachedSummary);
        }

        var summary = await BuildSummaryAsync(cancellationToken);
        memoryCache.Set(
            SummaryCacheKey,
            summary,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = SummaryCacheDuration,
                Size = 1
            });

        return ApiResponse<AdminDashboardSummaryDto>.SuccessResponse(summary);
    }

    private async Task<AdminDashboardSummaryDto> BuildSummaryAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var startDate = DateOnly.FromDateTime(now.AddDays(-13).UtcDateTime);

        var totalUsers = await dbContext.Users.CountAsync(cancellationToken);
        var activeUsers = await dbContext.Users.CountAsync(user => user.IsActive, cancellationToken);
        var disabledUsers = totalUsers - activeUsers;
        var totalVideos = await dbContext.Videos.CountAsync(video => video.DeletedAt == null, cancellationToken);
        var completedVideos = await dbContext.Videos.CountAsync(video => video.DeletedAt == null && video.Status == VideoStatus.Completed, cancellationToken);
        var failedVideos = await dbContext.Videos.CountAsync(video => video.DeletedAt == null && video.Status == VideoStatus.Failed, cancellationToken);
        var pendingJobs = await dbContext.AnalysisJobs.CountAsync(job => PendingJobStatuses.Contains(job.Status), cancellationToken);
        var failedJobs = await dbContext.AnalysisJobs.CountAsync(job => job.Status == JobStatus.Failed, cancellationToken);
        var providerTotal = await dbContext.AiProviderRequests.CountAsync(cancellationToken);
        var providerPending = await dbContext.AiProviderRequests.CountAsync(request => request.RequestCompletedAt == null, cancellationToken);

        var videoStatuses = await dbContext.Videos
            .AsNoTracking()
            .Where(video => video.DeletedAt == null)
            .GroupBy(video => video.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);

        var jobStatuses = await dbContext.AnalysisJobs
            .AsNoTracking()
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);

        var requestStatuses = await dbContext.AiProviderRequests
            .AsNoTracking()
            .GroupBy(request => request.Status)
            .Select(group => new AdminStatusCountDto { Status = group.Key, Count = group.LongCount() })
            .ToListAsync(cancellationToken);

        var uploadActivity = await BuildDailyActivityAsync(
            dbContext.Videos.AsNoTracking().Where(video => video.DeletedAt == null).Select(video => video.CreatedAt),
            startDate,
            cancellationToken);

        var analysisActivity = await BuildDailyActivityAsync(
            dbContext.AnalysisJobs.AsNoTracking().Select(job => job.CreatedAt),
            startDate,
            cancellationToken);

        var topUsers = await dbContext.Users
            .AsNoTracking()
            .Select(user => new AdminTopUserDto
            {
                UserId = user.Id,
                Name = user.Name,
                Email = user.Email,
                UploadCount = user.Videos.LongCount(video => video.DeletedAt == null)
            })
            .OrderByDescending(user => user.UploadCount)
            .ThenBy(user => user.Name)
            .Take(5)
            .ToListAsync(cancellationToken);

        var recentVideos = await dbContext.Videos
            .AsNoTracking()
            .Include(video => video.User)
            .Where(video => video.DeletedAt == null)
            .OrderByDescending(video => video.CreatedAt)
            .Take(6)
            .Select(video => new AdminRecentActivityDto
            {
                Type = "Upload",
                Title = video.OriginalName,
                Description = video.User.Email + " uploaded a video.",
                CreatedAt = video.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var recentFailures = await dbContext.AnalysisJobs
            .AsNoTracking()
            .Include(job => job.Video)
            .Where(job => job.Status == JobStatus.Failed)
            .OrderByDescending(job => job.UpdatedAt)
            .Take(4)
            .Select(job => new AdminRecentActivityDto
            {
                Type = "Failure",
                Title = job.Video.OriginalName,
                Description = job.ErrorCode ?? job.ErrorMessage ?? "Analysis failed.",
                CreatedAt = job.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var externalRequests = await BuildExternalSummaryAsync(now, monthStart, providerTotal, providerPending, cancellationToken);
        var retentionCleanup = await BuildCleanupSummaryAsync(now, cancellationToken);

        var summary = new AdminDashboardSummaryDto
        {
            Metrics =
            [
                new() { Key = "totalUsers", Label = "Total users", Value = totalUsers },
                new() { Key = "activeUsers", Label = "Active users", Value = activeUsers, Tone = "success" },
                new() { Key = "disabledUsers", Label = "Disabled users", Value = disabledUsers, Tone = disabledUsers > 0 ? "warning" : "default" },
                new() { Key = "totalVideos", Label = "Total videos", Value = totalVideos },
                new() { Key = "completedVideos", Label = "Completed videos", Value = completedVideos, Tone = "success" },
                new() { Key = "failedVideos", Label = "Failed videos", Value = failedVideos, Tone = failedVideos > 0 ? "danger" : "default" },
                new() { Key = "pendingJobs", Label = "Pending jobs", Value = pendingJobs, Tone = pendingJobs > 0 ? "warning" : "default" },
                new() { Key = "failedJobs", Label = "Failed jobs", Value = failedJobs, Tone = failedJobs > 0 ? "danger" : "default" },
                new() { Key = "externalRequests", Label = "External requests", Value = providerTotal },
                new() { Key = "pendingExternalRequests", Label = "Pending external", Value = providerPending, Tone = providerPending > 0 ? "warning" : "default" }
            ],
            VideoStatuses = videoStatuses
                .Select(item => new AdminStatusCountDto { Status = item.Status.ToString(), Count = item.Count })
                .OrderBy(item => item.Status)
                .ToList(),
            JobStatuses = jobStatuses
                .Select(item => new AdminStatusCountDto { Status = item.Status.ToString(), Count = item.Count })
                .OrderBy(item => item.Status)
                .ToList(),
            RequestStatuses = requestStatuses,
            UploadActivity = uploadActivity,
            AnalysisActivity = analysisActivity,
            TopUsers = topUsers,
            RecentActivity = recentVideos
                .Concat(recentFailures)
                .OrderByDescending(item => item.CreatedAt)
                .Take(8)
                .ToList(),
            ExternalRequests = externalRequests,
            RetentionCleanup = retentionCleanup
        };

        return summary;
    }

    public async Task<ApiResponse<PagedResponse<AdminUserListItemDto>>> GetUsersAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(user => user.Name.ToLower().Contains(term) || user.Email.ToLower().Contains(term));
        }

        query = status?.Trim().ToLowerInvariant() switch
        {
            "active" => query.Where(user => user.IsActive),
            "disabled" => query.Where(user => !user.IsActive),
            "unconfirmed" => query.Where(user => !user.EmailConfirmed),
            _ => query
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(user => user.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new AdminUserListItemDto
            {
                UserId = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role.ToString(),
                IsActive = user.IsActive,
                EmailConfirmed = user.EmailConfirmed,
                TotalVideos = user.Videos.LongCount(video => video.DeletedAt == null),
                CompletedVideos = user.Videos.LongCount(video => video.DeletedAt == null && video.Status == VideoStatus.Completed),
                FailedVideos = user.Videos.LongCount(video => video.DeletedAt == null && video.Status == VideoStatus.Failed),
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResponse<AdminUserListItemDto>>.SuccessResponse(ToPaged(items, page, pageSize, totalCount));
    }

    public async Task<ApiResponse<AdminUserListItemDto>> UpdateUserStatusAsync(
        long userId,
        long currentAdminId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        if (userId == currentAdminId && !isActive)
        {
            return ApiResponse<AdminUserListItemDto>.ErrorResponse("You cannot disable your own admin account.");
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(existing => existing.Id == userId, cancellationToken);
        if (user is null)
        {
            return ApiResponse<AdminUserListItemDto>.ErrorResponse("User was not found.");
        }

        user.IsActive = isActive;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        if (!isActive)
        {
            var now = DateTimeOffset.UtcNow;
            var tokens = await dbContext.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAt == null && token.ExpiresAt > now)
                .ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.RevokedAt = now;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        memoryCache.Remove(SummaryCacheKey);

        var response = new AdminUserListItemDto
        {
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role.ToString(),
            IsActive = user.IsActive,
            EmailConfirmed = user.EmailConfirmed,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };

        return ApiResponse<AdminUserListItemDto>.SuccessResponse(response, isActive ? "User account enabled." : "User account disabled.");
    }

    public async Task<ApiResponse<AdminManualSubscriptionGrantDto>> AssignUserRequestsAsync(
        long userId,
        long currentAdminId,
        AdminAssignUserRequestsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (userId <= 0)
        {
            return ApiResponse<AdminManualSubscriptionGrantDto>.ErrorResponse("User was not found.");
        }

        if (request.ScanLimit is < 1 or > 1000)
        {
            return ApiResponse<AdminManualSubscriptionGrantDto>.ErrorResponse("Manual request count must be between 1 and 1000.");
        }

        if (request.ValidityDays is < 1 or > 365)
        {
            return ApiResponse<AdminManualSubscriptionGrantDto>.ErrorResponse("Manual request validity must be between 1 and 365 days.");
        }

        if (!string.IsNullOrWhiteSpace(request.Notes) && request.Notes.Length > 500)
        {
            return ApiResponse<AdminManualSubscriptionGrantDto>.ErrorResponse("Notes cannot be longer than 500 characters.");
        }

        var user = await dbContext.Users.FirstOrDefaultAsync(existing => existing.Id == userId, cancellationToken);
        if (user is null)
        {
            return ApiResponse<AdminManualSubscriptionGrantDto>.ErrorResponse("User was not found.");
        }

        if (!user.IsActive)
        {
            return ApiResponse<AdminManualSubscriptionGrantDto>.ErrorResponse("Manual requests cannot be assigned to a disabled user.");
        }

        if (user.Role == UserRole.Admin)
        {
            return ApiResponse<AdminManualSubscriptionGrantDto>.ErrorResponse("Admin accounts already have internal unlimited scan access.");
        }

        var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await using (transaction)
        {
            var now = DateTimeOffset.UtcNow;
            var activeSubscriptions = await dbContext.UserSubscriptions
                .Include(subscription => subscription.SubscriptionPlan)
                .Where(subscription =>
                    subscription.UserId == userId &&
                    subscription.Status == SubscriptionStatuses.Active &&
                    subscription.StartsAt <= now &&
                    subscription.ExpiresAt > now &&
                    subscription.SubscriptionPlan.Code != SubscriptionPlanCodes.Free)
                .ToListAsync(cancellationToken);

            foreach (var subscription in activeSubscriptions)
            {
                subscription.Status = SubscriptionStatuses.Cancelled;
                subscription.CancelledAt = now;
            }

            var plan = new SubscriptionPlan
            {
                Code = GenerateManualPlanCode(now),
                Name = $"Manual Admin Grant - User {user.Id}",
                PriceAmount = 0m,
                Currency = "PKR",
                ScanLimit = request.ScanLimit,
                MaxVideoSizeBytes = request.AllowsDetailedScan
                    ? VideoUploadSizeLimits.ProMaxVideoSizeBytes
                    : VideoUploadSizeLimits.PlusMaxVideoSizeBytes,
                AllowsSmartScan = true,
                AllowsDetailedScan = request.AllowsDetailedScan,
                ValidityDays = request.ValidityDays,
                IsActive = true,
                SortOrder = 1000
            };

            var manualSubscription = new UserSubscription
            {
                UserId = userId,
                SubscriptionPlan = plan,
                Status = SubscriptionStatuses.Active,
                StartsAt = now,
                ExpiresAt = now.AddDays(request.ValidityDays),
                ActivatedAt = now
            };

            dbContext.SubscriptionPlans.Add(plan);
            dbContext.UserSubscriptions.Add(manualSubscription);
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditLogService.LogAsync(new AuditLogCreateDto
            {
                UserId = currentAdminId,
                Category = "Admin",
                Action = "ManualRequestsAssigned",
                Severity = "Information",
                Message = $"Admin assigned {request.ScanLimit} manual requests to {user.Email}.",
                ResourceType = "User",
                ResourceId = user.Id.ToString(),
                DetailsJson = JsonSerializer.Serialize(new
                {
                    targetUserId = user.Id,
                    targetEmail = user.Email,
                    subscriptionId = manualSubscription.Id,
                    planCode = plan.Code,
                    scanLimit = plan.ScanLimit,
                    validityDays = request.ValidityDays,
                    allowsDetailedScan = request.AllowsDetailedScan,
                    cancelledActiveSubscriptionIds = activeSubscriptions.Select(existing => existing.Id),
                    notes = request.Notes
                }, SerializerOptions)
            }, cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            memoryCache.Remove(SummaryCacheKey);

            var dto = new AdminManualSubscriptionGrantDto
            {
                UserId = user.Id,
                UserEmail = user.Email,
                SubscriptionId = manualSubscription.Id,
                PlanCode = plan.Code,
                PlanName = plan.Name,
                ScanLimit = plan.ScanLimit,
                UsedScans = 0,
                ReservedScans = 0,
                RemainingScans = plan.ScanLimit,
                AllowsDetailedScan = plan.AllowsDetailedScan,
                StartsAt = manualSubscription.StartsAt,
                ExpiresAt = manualSubscription.ExpiresAt
            };

            return ApiResponse<AdminManualSubscriptionGrantDto>.SuccessResponse(dto, "Manual requests assigned successfully.");
        }
    }

    public async Task<ApiResponse<PagedResponse<AdminVideoListItemDto>>> GetVideosAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Videos
            .AsNoTracking()
            .Where(video => video.DeletedAt == null)
            .Include(video => video.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(video =>
                video.OriginalName.ToLower().Contains(term)
                || video.User.Email.ToLower().Contains(term)
                || video.User.Name.ToLower().Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<VideoStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(video => video.Status == parsedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var videos = await query
            .Include(video => video.AnalysisJobs)
            .Include(video => video.AiResults)
            .OrderByDescending(video => video.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = videos.Select(MapVideoListItem).ToList();

        return ApiResponse<PagedResponse<AdminVideoListItemDto>>.SuccessResponse(ToPaged(items, page, pageSize, totalCount));
    }

    public async Task<ApiResponse<AdminVideoDetailDto>> GetVideoDetailAsync(
        long videoId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .AsNoTracking()
            .Include(existing => existing.User)
            .Include(existing => existing.MetadataResult)
            .Include(existing => existing.SourceMatches)
            .Include(existing => existing.AnalysisJobs)
            .Include(existing => existing.AiResults)
                .ThenInclude(result => result.EvidenceItems)
            .FirstOrDefaultAsync(existing => existing.Id == videoId && existing.DeletedAt == null, cancellationToken);

        if (video is null)
        {
            return ApiResponse<AdminVideoDetailDto>.ErrorResponse("Video was not found.");
        }

        var latestResult = video.AiResults
            .OrderByDescending(result => result.CreatedAt)
            .FirstOrDefault();

        var detail = new AdminVideoDetailDto
        {
            Video = MapVideoListItem(video),
            Metadata = video.MetadataResult is null ? null : new AdminMetadataSummaryDto
            {
                DurationSeconds = video.MetadataResult.DurationSeconds,
                Resolution = video.MetadataResult.Resolution,
                Fps = video.MetadataResult.Fps,
                Codec = video.MetadataResult.Codec,
                AudioCodec = video.MetadataResult.AudioCodec,
                Bitrate = video.MetadataResult.Bitrate,
                Encoder = video.MetadataResult.Encoder,
                CreationTime = video.MetadataResult.CreationTime,
                HasMissingMetadata = video.MetadataResult.HasMissingMetadata
            },
            Analysis = latestResult is null ? null : new AdminAnalysisSummaryDto
            {
                AiResultId = latestResult.Id,
                Label = latestResult.Label.ToString(),
                FinalScore = latestResult.FinalScore,
                Confidence = latestResult.Confidence,
                VisualScore = latestResult.VisualScore,
                MetadataScore = latestResult.MetadataScore,
                TemporalScore = latestResult.TemporalScore,
                Summary = latestResult.Summary,
                Provider = latestResult.Provider,
                ProviderMode = latestResult.ProviderMode,
                FinalDecisionSource = latestResult.FinalDecisionSource,
                ExternalProviderName = latestResult.ExternalProviderName,
                ExternalProviderStatus = latestResult.ExternalProviderStatus,
                FallbackUsed = latestResult.FallbackUsed,
                FallbackReason = latestResult.FallbackReason,
                CreatedAt = latestResult.CreatedAt
            },
            Evidence = latestResult?.EvidenceItems
                .OrderByDescending(item => item.Severity)
                .ThenBy(item => item.Id)
                .Select(item => new AdminEvidenceItemDto
                {
                    Type = item.Type.ToString(),
                    Severity = item.Severity.ToString(),
                    Title = item.Title,
                    Description = item.Description,
                    ScoreImpact = item.ScoreImpact,
                    TimestampSeconds = item.TimestampSeconds
                })
                .ToList() ?? [],
            OriginMatches = video.SourceMatches
                .OrderBy(match => match.Rank)
                .Select(match => new AdminSourceMatchDto
                {
                    Platform = match.Platform,
                    Title = match.Title,
                    UploadDatetime = match.UploadDatetime,
                    SimilarityScore = match.SimilarityScore,
                    Confidence = match.Confidence.ToString(),
                    Rank = match.Rank
                })
                .ToList(),
            Jobs = video.AnalysisJobs
                .OrderByDescending(job => job.CreatedAt)
                .Select(job => new AdminJobListItemDto
                {
                    JobId = job.Id,
                    VideoId = job.VideoId,
                    VideoName = video.OriginalName,
                    UserId = video.UserId,
                    UserEmail = video.User.Email,
                    Status = job.Status.ToString(),
                    Progress = job.Progress,
                    CurrentStep = job.CurrentStep,
                    ErrorMessage = job.ErrorMessage,
                    ErrorCode = job.ErrorCode,
                    RetryCount = job.RetryCount,
                    MaxRetryCount = job.MaxRetryCount,
                    CreatedAt = job.CreatedAt,
                    UpdatedAt = job.UpdatedAt,
                    StartedAt = job.StartedAt,
                    CompletedAt = job.CompletedAt
                })
                .ToList()
        };

        return ApiResponse<AdminVideoDetailDto>.SuccessResponse(detail);
    }

    public async Task<ApiResponse<AdminVideoFileDto>> GetVideoFileAsync(
        long videoId,
        CancellationToken cancellationToken = default)
    {
        var video = await dbContext.Videos
            .AsNoTracking()
            .Where(existing => existing.Id == videoId && existing.DeletedAt == null && existing.Status != VideoStatus.Deleted)
            .Select(existing => new AdminVideoFileDto
            {
                ObjectKey = existing.FileUrl,
                FileName = existing.OriginalName,
                ContentType = existing.ContentType ?? "application/octet-stream"
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (video is null)
        {
            return ApiResponse<AdminVideoFileDto>.ErrorResponse("Video was not found.");
        }

        if (string.IsNullOrWhiteSpace(video.ObjectKey))
        {
            return ApiResponse<AdminVideoFileDto>.ErrorResponse("The original video file is no longer available.");
        }

        return ApiResponse<AdminVideoFileDto>.SuccessResponse(video);
    }

    public async Task<ApiResponse<PagedResponse<AdminJobListItemDto>>> GetJobsAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.AnalysisJobs
            .AsNoTracking()
            .Include(job => job.Video)
                .ThenInclude(video => video.User)
            .Where(job => job.Video.DeletedAt == null);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(job => job.Status == parsedStatus);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(job => job.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(job => new AdminJobListItemDto
            {
                JobId = job.Id,
                VideoId = job.VideoId,
                VideoName = job.Video.OriginalName,
                UserId = job.Video.UserId,
                UserEmail = job.Video.User.Email,
                Status = job.Status.ToString(),
                Progress = job.Progress,
                CurrentStep = job.CurrentStep,
                ErrorMessage = job.ErrorMessage,
                ErrorCode = job.ErrorCode,
                RetryCount = job.RetryCount,
                MaxRetryCount = job.MaxRetryCount,
                CreatedAt = job.CreatedAt,
                UpdatedAt = job.UpdatedAt,
                StartedAt = job.StartedAt,
                CompletedAt = job.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResponse<AdminJobListItemDto>>.SuccessResponse(ToPaged(items, page, pageSize, totalCount));
    }

    public async Task<ApiResponse<PagedResponse<AdminProviderRequestListItemDto>>> GetProviderRequestsAsync(
        int page,
        int pageSize,
        string? status,
        long? userId,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.AiProviderRequests
            .AsNoTracking()
            .Include(request => request.Video)
            .Include(request => request.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            query = query.Where(request => request.Status.ToLower() == normalizedStatus);
        }

        if (userId is > 0)
        {
            query = query.Where(request => request.UserId == userId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(request => request.RequestStartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(request => new AdminProviderRequestListItemDto
            {
                RequestId = request.Id,
                VideoId = request.VideoId,
                VideoName = request.Video.OriginalName,
                UserId = request.UserId,
                UserEmail = request.User.Email,
                ProviderName = request.ProviderName,
                ProviderMode = request.ProviderMode,
                Status = request.Status,
                HttpStatusCode = request.HttpStatusCode,
                DurationMs = request.DurationMs,
                ErrorMessage = request.ErrorMessage,
                RequestStartedAt = request.RequestStartedAt,
                RequestCompletedAt = request.RequestCompletedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResponse<AdminProviderRequestListItemDto>>.SuccessResponse(ToPaged(items, page, pageSize, totalCount));
    }

    public async Task<ApiResponse<PagedResponse<AdminProviderRequestUserSummaryDto>>> GetProviderRequestUsersAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.AiProviderRequests
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(request => request.User.Name.ToLower().Contains(term) || request.User.Email.ToLower().Contains(term));
        }

        var groupedQuery = query.GroupBy(request => new
        {
            request.UserId,
            request.User.Name,
            request.User.Email
        });

        var totalCount = await groupedQuery.CountAsync(cancellationToken);
        var groupedRows = await groupedQuery
            .OrderByDescending(group => group.Max(request => request.RequestStartedAt))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(group => new
            {
                group.Key.UserId,
                group.Key.Name,
                group.Key.Email,
                TotalRequests = group.LongCount(),
                PendingRequests = group.LongCount(request => request.RequestCompletedAt == null),
                CompletedRequests = group.LongCount(request => request.RequestCompletedAt != null && request.ErrorMessage == null),
                FailedRequests = group.LongCount(request => request.ErrorMessage != null || (request.HttpStatusCode != null && request.HttpStatusCode >= 400)),
                LatestRequestAt = group.Max(request => (DateTimeOffset?)request.RequestStartedAt)
            })
            .ToListAsync(cancellationToken);

        var userIds = groupedRows.Select(row => row.UserId).ToArray();
        var latestRequestVideos = await dbContext.AiProviderRequests
            .AsNoTracking()
            .Where(request => userIds.Contains(request.UserId))
            .OrderByDescending(request => request.RequestStartedAt)
            .Select(request => new
            {
                request.UserId,
                VideoName = request.Video.OriginalName
            })
            .ToListAsync(cancellationToken);

        var latestVideoByUser = latestRequestVideos
            .GroupBy(request => request.UserId)
            .ToDictionary(group => group.Key, group => group.First().VideoName);

        var users = groupedRows
            .Select(row => new AdminProviderRequestUserSummaryDto
            {
                UserId = row.UserId,
                Name = row.Name,
                Email = row.Email,
                TotalRequests = row.TotalRequests,
                PendingRequests = row.PendingRequests,
                CompletedRequests = row.CompletedRequests,
                FailedRequests = row.FailedRequests,
                LatestRequestAt = row.LatestRequestAt,
                LatestVideoName = latestVideoByUser.GetValueOrDefault(row.UserId)
            })
            .ToList();

        return ApiResponse<PagedResponse<AdminProviderRequestUserSummaryDto>>.SuccessResponse(ToPaged(users, page, pageSize, totalCount));
    }

    private async Task<AdminExternalRequestSummaryDto> BuildExternalSummaryAsync(
        DateTimeOffset now,
        DateTimeOffset monthStart,
        long totalRequests,
        long pendingRequests,
        CancellationToken cancellationToken)
    {
        var completedRequests = await dbContext.AiProviderRequests
            .CountAsync(request => request.RequestCompletedAt != null && request.ErrorMessage == null, cancellationToken);
        var failedRequests = await dbContext.AiProviderRequests
            .CountAsync(request => request.ErrorMessage != null || (request.HttpStatusCode != null && request.HttpStatusCode >= 400), cancellationToken);

        var usage = await dbContext.ApiUsageMonthly
            .AsNoTracking()
            .Where(item => item.ProviderName == BitMindProviderName
                && item.Year == monthStart.Year
                && item.Month == monthStart.Month)
            .FirstOrDefaultAsync(cancellationToken);

        var monthlyUsed = usage?.RequestCount
            ?? await dbContext.AiProviderRequests.CountAsync(request =>
                request.ProviderName == BitMindProviderName
                && request.RequestStartedAt >= monthStart
                && request.RequestStartedAt <= now,
                cancellationToken);
        var monthlyQuota = usage?.QuotaLimit ?? 0;
        var circuit = providerCircuitBreaker.GetSnapshot(BitMindProviderName);

        return new AdminExternalRequestSummaryDto
        {
            ProviderName = BitMindProviderName,
            TotalRequests = totalRequests,
            PendingRequests = pendingRequests,
            CompletedRequests = completedRequests,
            FailedRequests = failedRequests,
            MonthlyQuotaLimit = monthlyQuota,
            MonthlyUsed = monthlyUsed,
            MonthlyRemaining = monthlyQuota <= 0 ? 0 : Math.Max(0, monthlyQuota - monthlyUsed),
            MonthlySuccess = usage?.SuccessCount ?? 0,
            MonthlyFailed = usage?.FailedCount ?? 0,
            HealthStatus = circuit.IsOpen ? "Temporarily paused" : "Available",
            CircuitOpen = circuit.IsOpen,
            CircuitConsecutiveFailures = circuit.ConsecutiveFailures,
            CircuitOpenUntil = circuit.OpenUntil
        };
    }

    private async Task<AdminCleanupSummaryDto> BuildCleanupSummaryAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var last24Hours = now.AddHours(-24);
        var temporaryFrameCutoff = now.Subtract(_videoProcessingOptions.TemporaryFileRetention);
        var originalVideoFallbackCutoff = now.Subtract(_videoProcessingOptions.OriginalVideoRetention);
        var detailedPayloadCutoff = now.Subtract(_videoProcessingOptions.DetailedResultRetention);

        var latestRun = await dbContext.RetentionCleanupRuns
            .AsNoTracking()
            .OrderByDescending(run => run.StartedAt)
            .ThenByDescending(run => run.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var runsLast24Hours = await dbContext.RetentionCleanupRuns
            .AsNoTracking()
            .CountAsync(run => run.StartedAt >= last24Hours, cancellationToken);

        var failuresLast24Hours = await dbContext.RetentionCleanupRuns
            .AsNoTracking()
            .CountAsync(run => run.StartedAt >= last24Hours
                && (run.Status != "Succeeded" || run.FailureCount > 0), cancellationToken);

        var pendingTemporaryFrameCleanup = await dbContext.VideoFrames
            .AsNoTracking()
            .CountAsync(frame => frame.CreatedAt <= temporaryFrameCutoff && frame.FrameUrl != string.Empty, cancellationToken);

        var pendingOriginalVideoCleanup = await dbContext.Videos
            .AsNoTracking()
            .CountAsync(video => video.FileUrl != string.Empty
                && ((video.RetentionDeleteAt != null && video.RetentionDeleteAt <= now)
                    || (video.RetentionDeleteAt == null && video.CreatedAt <= originalVideoFallbackCutoff)), cancellationToken);

        var pendingDetailedPayloadCleanup = await dbContext.AiResults
            .AsNoTracking()
            .CountAsync(result => result.CreatedAt <= detailedPayloadCutoff
                && (result.RawModelOutputJson != "{}"
                    || result.LocalResultJson != null
                    || result.HybridResultJson != null
                    || result.ExternalRawResponseJson != null), cancellationToken);

        return new AdminCleanupSummaryDto
        {
            LastRunAt = latestRun?.CompletedAt ?? latestRun?.StartedAt,
            LastStatus = latestRun?.Status ?? "NotRun",
            LastDurationMs = latestRun?.DurationMs ?? 0,
            LastFailureCount = latestRun?.FailureCount ?? 0,
            RunsLast24Hours = runsLast24Hours,
            FailuresLast24Hours = failuresLast24Hours,
            PendingTemporaryFrameCleanup = pendingTemporaryFrameCleanup,
            PendingOriginalVideoCleanup = pendingOriginalVideoCleanup,
            PendingDetailedPayloadCleanup = pendingDetailedPayloadCleanup
        };
    }

    private static async Task<IReadOnlyList<AdminDailyActivityDto>> BuildDailyActivityAsync(
        IQueryable<DateTimeOffset> dates,
        DateOnly startDate,
        CancellationToken cancellationToken)
    {
        var startDateTime = startDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var rows = await dates
            .Where(date => date >= new DateTimeOffset(startDateTime))
            .ToListAsync(cancellationToken);

        var byDate = rows
            .GroupBy(date => DateOnly.FromDateTime(date.UtcDateTime))
            .ToDictionary(group => group.Key, group => (long)group.Count());
        return Enumerable.Range(0, 14)
            .Select(offset => startDate.AddDays(offset))
            .Select(date => new AdminDailyActivityDto
            {
                Date = date,
                Count = byDate.TryGetValue(date, out var count) ? count : 0
            })
            .ToList();
    }

    private static string GenerateManualPlanCode(DateTimeOffset now)
    {
        return $"{ManualPlanPrefix}-{now:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..29].ToUpperInvariant();
    }

    private static AdminVideoListItemDto MapVideoListItem(Video video)
    {
        var latestJob = video.AnalysisJobs
            .OrderByDescending(job => job.CreatedAt)
            .ThenByDescending(job => job.Id)
            .FirstOrDefault();
        var latestResult = video.AiResults
            .OrderByDescending(result => result.CreatedAt)
            .ThenByDescending(result => result.Id)
            .FirstOrDefault();

        return new AdminVideoListItemDto
        {
            VideoId = video.Id,
            UserId = video.UserId,
            OwnerName = video.User.Name,
            OwnerEmail = video.User.Email,
            OriginalName = video.OriginalName,
            FileSize = video.FileSize,
            ContentType = video.ContentType,
            Status = video.Status.ToString(),
            CreatedAt = video.CreatedAt,
            UpdatedAt = video.UpdatedAt,
            LatestJobStatus = latestJob?.Status.ToString(),
            LatestJobProgress = latestJob?.Progress,
            FinalVerdict = latestResult?.Label.ToString(),
            AiGeneratedProbability = latestResult is null ? null : Math.Round(latestResult.FinalScore * 100m, 2),
            Confidence = latestResult is null ? null : Math.Round(latestResult.Confidence * 100m, 2),
            ExternalVerificationUsed = latestResult?.ExternalProviderName is not null
        };
    }

    private static PagedResponse<T> ToPaged<T>(IReadOnlyList<T> items, int page, int pageSize, int totalCount)
    {
        return new PagedResponse<T>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
