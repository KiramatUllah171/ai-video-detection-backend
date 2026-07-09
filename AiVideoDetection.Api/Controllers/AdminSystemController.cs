using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Processing;
using AiVideoDetection.Application.Videos.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/admin/system")]
public class AdminSystemController(
    IVideoProcessingToolValidator videoProcessingToolValidator,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("ffmpeg-check")]
    [ProducesResponseType(typeof(ApiResponse<VideoProcessingToolCheckResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<VideoProcessingToolCheckResult>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<VideoProcessingToolCheckResult>>> FfmpegCheck(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return Forbid();
        }

        var result = await videoProcessingToolValidator.ValidateAsync(cancellationToken);
        return Ok(ApiResponse<VideoProcessingToolCheckResult>.SuccessResponse(result));
    }
}
