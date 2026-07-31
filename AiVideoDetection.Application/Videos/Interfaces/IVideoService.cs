using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IVideoService
{
    Task<ApiResponse<UploadVideoResponse>> UploadAsync(
        UploadVideoRequest request,
        long currentUserId,
        string? ipAddress,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResponse<VideoHistoryItemDto>>> GetHistoryAsync(
        long currentUserId,
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<VideoDetailDto>> GetVideoDetailAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<MetadataResultDto>> GetMetadataAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<VideoFrameDto>>> GetFramesAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AnalysisResultDto>> GetAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<UploadVideoResponse>> ReanalyzeAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<UploadVideoResponse>> RetryAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<JobStatusDto>> CancelAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<JobStatusDto>> PauseAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<JobStatusDto>> ResumeAnalysisAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<EvidenceItemDto>>> GetEvidenceAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<bool>> DeleteVideoAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);
}
