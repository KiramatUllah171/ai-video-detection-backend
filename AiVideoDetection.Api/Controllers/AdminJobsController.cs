using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/jobs")]
public class AdminJobsController(IJobService jobService) : ControllerBase
{
    [HttpPost("{jobId:long}/retry")]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse<JobStatusDto>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<JobStatusDto>>> Retry(long jobId, CancellationToken cancellationToken)
    {
        var response = await jobService.RetryJobAsync(jobId, cancellationToken);
        return response.Success ? Ok(response) : BadRequest(response);
    }
}
