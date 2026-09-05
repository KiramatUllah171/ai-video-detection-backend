using System.Security.Claims;
using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Admin.Interfaces;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Subscriptions;
using AiVideoDetection.Application.Videos;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmailConfirmed")]
[Route("api/videos")]
public class VideosController(
    IVideoService videoService,
    IAnalysisReportService analysisReportService,
    IInternalVideoMatchingService internalVideoMatchingService,
    IAuthorizationService authorizationService,
    IAuthThrottleService authThrottleService,
    IAuditLogService auditLogService,
    IOptions<AuthSecurityOptions> authSecurityOptions) : ControllerBase
{
    private const string TooManyRequestsMessage = "Too many requests. Please wait a moment and try again.";
    private readonly AuthSecurityOptions _authSecurityOptions = authSecurityOptions.Value;

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(VideoUploadSizeLimits.MultipartRequestBodyLimitBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = VideoUploadSizeLimits.MultipartRequestBodyLimitBytes)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UploadVideoResponse>>> Upload(
        [FromForm] UploadVideoRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<UploadVideoResponse>.ErrorResponse("Unauthorized."));
        }

        var response = await videoService.UploadAsync(request, currentUserId, GetIpAddress(), cancellationToken);
        return ToActionResult(response);
    }

    [HttpGet("history")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<VideoHistoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<VideoHistoryItemDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResponse<VideoHistoryItemDto>>>> History(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<PagedResponse<VideoHistoryItemDto>>.ErrorResponse("Unauthorized."));
        }

        var response = await videoService.GetHistoryAsync(currentUserId, page, pageSize, status, cancellationToken);
        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        return Ok(response);
    }

    [HttpGet("{videoId:long}")]
    [ProducesResponseType(typeof(ApiResponse<VideoDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<VideoDetailDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<VideoDetailDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<VideoDetailDto>>> Detail(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<VideoDetailDto>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<VideoDetailDto>());
        }

        var response = await videoService.GetVideoDetailAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    [HttpGet("{videoId:long}/metadata")]
    [ProducesResponseType(typeof(ApiResponse<MetadataResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<MetadataResultDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<MetadataResultDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<MetadataResultDto>>> Metadata(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<MetadataResultDto>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<MetadataResultDto>());
        }

        var response = await videoService.GetMetadataAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    [HttpGet("{videoId:long}/frames")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VideoFrameDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VideoFrameDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<VideoFrameDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<VideoFrameDto>>>> Frames(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<IReadOnlyList<VideoFrameDto>>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<IReadOnlyList<VideoFrameDto>>());
        }

        var response = await videoService.GetFramesAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    [HttpGet("{videoId:long}/analysis")]
    [ProducesResponseType(typeof(ApiResponse<AnalysisResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AnalysisResultDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<AnalysisResultDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AnalysisResultDto>>> Analysis(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<AnalysisResultDto>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<AnalysisResultDto>());
        }

        var response = await videoService.GetAnalysisAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    [HttpGet("{videoId:long}/report/pdf")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AnalysisReportFile>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<AnalysisReportFile>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadPdfReport(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<AnalysisReportFile>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessReportAsync(videoId))
        {
            return NotFound(VideoNotFound<AnalysisReportFile>());
        }

        var reportThrottle = authThrottleService.Check(
            "report-download-user-video",
            $"{currentUserId}:{videoId}",
            _authSecurityOptions.ReportDownloadPerReportPermitLimit,
            TimeSpan.FromMinutes(Math.Max(1, _authSecurityOptions.EmailThrottleWindowMinutes)));
        if (!reportThrottle.IsAllowed)
        {
            await auditLogService.LogAsync(new AuditLogCreateDto
            {
                UserId = currentUserId,
                UserName = User.FindFirstValue(ClaimTypes.Name),
                UserEmail = User.FindFirstValue(ClaimTypes.Email),
                Category = "Report",
                Action = "ReportDownloadThrottled",
                Severity = "Warning",
                Message = "PDF report download was blocked by report throttling.",
                ResourceType = "Video",
                ResourceId = videoId.ToString(),
                HttpMethod = Request.Method,
                Path = Request.Path.Value,
                StatusCode = StatusCodes.Status429TooManyRequests,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = Request.Headers.UserAgent.ToString()
            }, cancellationToken);

            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                ApiResponse<AnalysisReportFile>.ErrorResponse(TooManyRequestsMessage));
        }

        var response = await analysisReportService.GeneratePdfAsync(videoId, currentUserId, cancellationToken);
        if (!response.Success || response.Data is null)
        {
            if (response.Message.Contains("report retention period has ended", StringComparison.OrdinalIgnoreCase))
            {
                return StatusCode(StatusCodes.Status410Gone, response);
            }

            return NotFound(response);
        }

        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        return File(response.Data.Content, response.Data.ContentType, response.Data.FileName);
    }

    [HttpPost("{videoId:long}/reanalyze")]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UploadVideoResponse>>> Reanalyze(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<UploadVideoResponse>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<UploadVideoResponse>());
        }

        var response = await videoService.ReanalyzeAsync(videoId, currentUserId, cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("{videoId:long}/retry-analysis")]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<UploadVideoResponse>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<UploadVideoResponse>>> RetryAnalysis(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<UploadVideoResponse>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<UploadVideoResponse>());
        }

        var response = await videoService.RetryAnalysisAsync(videoId, currentUserId, cancellationToken);
        return ToActionResult(response);
    }

    [HttpPost("{videoId:long}/cancel-analysis")]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<JobStatusDto>>> CancelAnalysis(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<JobStatusDto>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<JobStatusDto>());
        }

        var response = await videoService.CancelAnalysisAsync(videoId, currentUserId, cancellationToken);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? NotFound(response)
            : BadRequest(response);
    }

    [HttpPost("{videoId:long}/pause-analysis")]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<JobStatusDto>>> PauseAnalysis(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<JobStatusDto>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<JobStatusDto>());
        }

        var response = await videoService.PauseAnalysisAsync(videoId, currentUserId, cancellationToken);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? NotFound(response)
            : BadRequest(response);
    }

    [HttpPost("{videoId:long}/resume-analysis")]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<JobStatusDto>>> ResumeAnalysis(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<JobStatusDto>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<JobStatusDto>());
        }

        var response = await videoService.ResumeAnalysisAsync(videoId, currentUserId, cancellationToken);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? NotFound(response)
            : BadRequest(response);
    }

    [HttpGet("{videoId:long}/evidence")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EvidenceItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EvidenceItemDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EvidenceItemDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<EvidenceItemDto>>>> Evidence(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<IReadOnlyList<EvidenceItemDto>>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<IReadOnlyList<EvidenceItemDto>>());
        }

        var response = await videoService.GetEvidenceAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    [HttpGet("{videoId:long}/origin-matches")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SourceMatchDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SourceMatchDto>>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<SourceMatchDto>>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SourceMatchDto>>>> OriginMatches(
        long videoId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<IReadOnlyList<SourceMatchDto>>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<IReadOnlyList<SourceMatchDto>>());
        }

        var response = await internalVideoMatchingService.GetMatchesAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    [HttpDelete("{videoId:long}")]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<bool>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(long videoId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentUserId))
        {
            return Unauthorized(ApiResponse<bool>.ErrorResponse("Unauthorized."));
        }

        if (!await CanAccessVideoAsync(videoId))
        {
            return NotFound(VideoNotFound<bool>());
        }

        var response = await videoService.DeleteVideoAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    private async Task<bool> CanAccessVideoAsync(long videoId)
    {
        var result = await authorizationService.AuthorizeAsync(User, videoId, "OwnVideoOnly");
        return result.Succeeded;
    }

    private async Task<bool> CanAccessReportAsync(long videoId)
    {
        var result = await authorizationService.AuthorizeAsync(User, videoId, "OwnReportOnly");
        return result.Succeeded;
    }

    private bool TryGetCurrentUserId(out long currentUserId)
    {
        return long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out currentUserId);
    }

    private string? GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }

    private ActionResult<ApiResponse<T>> ToActionResult<T>(ApiResponse<T> response)
    {
        if (response.Success)
        {
            return Ok(response);
        }

        if (response.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(response);
        }

        return response.ErrorCode switch
        {
            SubscriptionErrorCodes.FreeTrialExhausted
                or SubscriptionErrorCodes.ScanQuotaExhausted
                or SubscriptionErrorCodes.SubscriptionRequired
                or SubscriptionErrorCodes.DetailedScanNotAllowed => StatusCode(StatusCodes.Status402PaymentRequired, response),
            SubscriptionErrorCodes.VideoSizeLimitExceeded => StatusCode(StatusCodes.Status413PayloadTooLarge, response),
            SubscriptionErrorCodes.SubscriptionSecurityNotConfigured => StatusCode(StatusCodes.Status500InternalServerError, response),
            VideoInfrastructureErrorCodes.ServerStorageCapacityLow
                or VideoInfrastructureErrorCodes.AnalysisQueueUnavailable => StatusCode(StatusCodes.Status503ServiceUnavailable, response),
            VideoInfrastructureErrorCodes.UploadConcurrencyLimitReached => StatusCode(StatusCodes.Status429TooManyRequests, response),
            _ => BadRequest(response)
        };
    }

    private static ApiResponse<T> VideoNotFound<T>()
    {
        return ApiResponse<T>.ErrorResponse("Video was not found.");
    }
}
