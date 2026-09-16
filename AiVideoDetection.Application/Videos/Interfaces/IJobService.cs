using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IJobService
{
    Task<ApiResponse<JobStatusDto>> GetStatusByVideoIdAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<JobStatusDto>> GetGuestStatusByVideoIdAsync(
        long videoId,
        string guestAccessToken,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<JobStatusDto>> RetryJobAsync(
        long jobId,
        CancellationToken cancellationToken = default);
}
