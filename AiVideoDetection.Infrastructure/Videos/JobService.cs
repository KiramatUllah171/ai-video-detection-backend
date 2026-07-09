using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AiVideoDetection.Infrastructure.Videos;

public class JobService(
    AppDbContext dbContext,
    IAnalysisJobQueue analysisJobQueue,
    IJobLogService jobLogService) : IJobService
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

    public async Task<ApiResponse<JobStatusDto>> RetryJobAsync(
        long jobId,
        CancellationToken cancellationToken = default)
    {
        var job = await dbContext.AnalysisJobs
            .Include(existingJob => existingJob.Video)
            .FirstOrDefaultAsync(existingJob => existingJob.Id == jobId, cancellationToken);

        if (job is null)
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("Job was not found.");
        }

        if (job.Status != JobStatus.Failed)
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("Only failed jobs can be retried.");
        }

        if (job.RetryCount >= job.MaxRetryCount)
        {
            return ApiResponse<JobStatusDto>.ErrorResponse("Maximum retry count was reached.");
        }

        job.Status = JobStatus.Retrying;
        job.Progress = 0;
        job.CurrentStep = "Retry queued for media processing";
        job.ErrorCode = null;
        job.ErrorMessage = null;
        job.CompletedAt = null;

        if (job.Video is not null)
        {
            job.Video.Status = VideoStatus.Queued;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await jobLogService.LogAsync(job.Id, "RetryQueued", "Information", "Job retry was queued by an administrator.", null, cancellationToken);
        analysisJobQueue.RetryAnalysisJob(job.Id);

        return ApiResponse<JobStatusDto>.SuccessResponse(VideoService.MapJob(job), "Job retry queued.");
    }
}
