using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Application.Admin.Interfaces;

public interface IAdminDashboardService
{
    Task<ApiResponse<AdminDashboardSummaryDto>> GetSummaryAsync(CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResponse<AdminUserListItemDto>>> GetUsersAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AdminUserListItemDto>> UpdateUserStatusAsync(
        long userId,
        long currentAdminId,
        bool isActive,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AdminManualSubscriptionGrantDto>> GetUserRequestGrantAsync(
        string email,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AdminManualSubscriptionGrantDto>> AssignUserRequestsAsync(
        long currentAdminId,
        AdminAssignUserRequestsRequest request,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResponse<AdminVideoListItemDto>>> GetVideosAsync(
        int page,
        int pageSize,
        string? search,
        string? status,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AdminVideoDetailDto>> GetVideoDetailAsync(
        long videoId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<AdminVideoFileDto>> GetVideoFileAsync(
        long videoId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResponse<AdminJobListItemDto>>> GetJobsAsync(
        int page,
        int pageSize,
        string? status,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResponse<AdminProviderRequestListItemDto>>> GetProviderRequestsAsync(
        int page,
        int pageSize,
        string? status,
        long? userId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResponse<AdminProviderRequestUserSummaryDto>>> GetProviderRequestUsersAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default);
}
