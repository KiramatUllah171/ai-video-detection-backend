using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiVideoDetection.Infrastructure.Videos;

public class JobService(AppDbContext dbContext) : IJobService
{
    public async Task<ApiResponse<JobStatusDto>> GetStatusByVideoIdAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.AnalysisJobs
            .AsNoTracking()
            .Include(existingJob => existingJob.Video)
            .Where(existingJob => existingJob.VideoId == videoId
                && existingJob.Video.UserId == currentUserId
                && existingJob.Video.DeletedAt == null
                && existingJob.Video.Status != VideoStatus.Deleted)
            .OrderByDescending(existingJob => existingJob.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return job is null
            ? ApiResponse<JobStatusDto>.ErrorResponse("Job status was not found.")
            : ApiResponse<JobStatusDto>.SuccessResponse(VideoService.MapJob(job));
    }
}
