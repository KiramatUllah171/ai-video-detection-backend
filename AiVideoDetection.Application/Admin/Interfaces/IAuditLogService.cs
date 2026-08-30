using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Application.Admin.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        AuditLogCreateDto entry,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<PagedResponse<AdminAuditLogDto>>> GetLogsAsync(
        int page,
        int pageSize,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? severity,
        string? category,
        CancellationToken cancellationToken = default);
}
