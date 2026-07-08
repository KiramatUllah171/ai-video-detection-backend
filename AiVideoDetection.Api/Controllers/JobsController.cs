using System.Security.Claims;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/jobs")]
public class JobsController(IJobService jobService) : ControllerBase
{
    [HttpGet("{videoId:long}/status")]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<JobStatusDto>>> Status(long videoId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var currentUserId))
        {
            return Unauthorized(ApiResponse<JobStatusDto>.ErrorResponse("Unauthorized."));
        }

        var response = await jobService.GetStatusByVideoIdAsync(videoId, currentUserId, cancellationToken);
        return response.Success ? Ok(response) : NotFound(response);
    }
}
