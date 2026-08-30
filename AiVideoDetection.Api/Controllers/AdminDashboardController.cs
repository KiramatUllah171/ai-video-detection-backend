using System.Security.Claims;
using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Admin.Interfaces;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin")]
public sealed class AdminDashboardController(
    IAdminDashboardService adminDashboardService,
    IObjectStorageService objectStorageService,
    IAuditLogService auditLogService) : ControllerBase
{
    [HttpGet("dashboard/summary")]
    [ProducesResponseType(typeof(ApiResponse<AdminDashboardSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<AdminDashboardSummaryDto>>> Summary(CancellationToken cancellationToken)
    {
        var response = await adminDashboardService.GetSummaryAsync(cancellationToken);
        return Ok(response);
    }

    [HttpGet("users")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AdminUserListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminUserListItemDto>>>> Users(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var response = await adminDashboardService.GetUsersAsync(page, pageSize, search, status, cancellationToken);
        return Ok(response);
    }

    [HttpPatch("users/{userId:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<AdminUserListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AdminUserListItemDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<AdminUserListItemDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<AdminUserListItemDto>>> UpdateUserStatus(
        long userId,
        [FromBody] AdminUpdateUserStatusRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var currentAdminId))
        {
            return Unauthorized(ApiResponse<AdminUserListItemDto>.ErrorResponse("Unauthorized."));
        }

        var response = await adminDashboardService.UpdateUserStatusAsync(userId, currentAdminId, request.IsActive, cancellationToken);
        return response.Success ? Ok(response) : BadRequest(response);
    }

    [HttpGet("videos")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AdminVideoListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminVideoListItemDto>>>> Videos(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var response = await adminDashboardService.GetVideosAsync(page, pageSize, search, status, cancellationToken);
        return Ok(response);
    }

    [HttpGet("videos/{videoId:long}")]
    [ProducesResponseType(typeof(ApiResponse<AdminVideoDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AdminVideoDetailDto>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<AdminVideoDetailDto>>> VideoDetail(long videoId, CancellationToken cancellationToken)
    {
        var response = await adminDashboardService.GetVideoDetailAsync(videoId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }

    [HttpGet("videos/{videoId:long}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<AdminVideoFileDto>), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VideoFile(long videoId, CancellationToken cancellationToken)
    {
        var response = await adminDashboardService.GetVideoFileAsync(videoId, cancellationToken);
        if (!response.Success || response.Data is null)
        {
            return NotFound(response);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"admin-video-{Guid.NewGuid():N}{Path.GetExtension(response.Data.FileName)}");
        await objectStorageService.DownloadToAsync(response.Data.ObjectKey, tempPath, cancellationToken);

        var stream = new FileStream(
            tempPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.DeleteOnClose);

        Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        Response.Headers.Pragma = "no-cache";
        return File(stream, response.Data.ContentType, response.Data.FileName, enableRangeProcessing: true);
    }

    [HttpGet("jobs")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AdminJobListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminJobListItemDto>>>> Jobs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var response = await adminDashboardService.GetJobsAsync(page, pageSize, status, cancellationToken);
        return Ok(response);
    }

    [HttpGet("provider-requests")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AdminProviderRequestListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminProviderRequestListItemDto>>>> ProviderRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] long? userId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await adminDashboardService.GetProviderRequestsAsync(page, pageSize, status, userId, cancellationToken);
        return Ok(response);
    }

    [HttpGet("provider-request-users")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AdminProviderRequestUserSummaryDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminProviderRequestUserSummaryDto>>>> ProviderRequestUsers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var response = await adminDashboardService.GetProviderRequestUsersAsync(page, pageSize, search, cancellationToken);
        return Ok(response);
    }

    [HttpGet("logs")]
    [ProducesResponseType(typeof(ApiResponse<PagedResponse<AdminAuditLogDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResponse<AdminAuditLogDto>>>> Logs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? severity = null,
        [FromQuery] string? category = null,
        CancellationToken cancellationToken = default)
    {
        var response = await auditLogService.GetLogsAsync(page, pageSize, search, from, to, severity, category, cancellationToken);
        return Ok(response);
    }

    private bool TryGetCurrentUserId(out long currentUserId)
    {
        return long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out currentUserId);
    }
}
