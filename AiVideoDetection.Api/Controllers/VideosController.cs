using System.Security.Claims;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize(Policy = "EmailConfirmed")]
[Route("api/videos")]
public class VideosController(
    IVideoService videoService,
    IAnalysisReportService analysisReportService,
    IInternalVideoMatchingService internalVideoMatchingService) : ControllerBase
{
    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(524_288_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 524_288_000)]
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

        var response = await videoService.ReanalyzeAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
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

        var response = await videoService.RetryAnalysisAsync(videoId, currentUserId, cancellationToken);
        if (response.Success)
        {
            return Ok(response);
        }

        return response.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? NotFound(response)
            : BadRequest(response);
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

        var response = await videoService.DeleteVideoAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
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
        return response.Success ? Ok(response) : BadRequest(response);
    }
}
