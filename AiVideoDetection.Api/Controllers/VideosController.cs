using System.Security.Claims;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/videos")]
public class VideosController(IVideoService videoService) : ControllerBase
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
