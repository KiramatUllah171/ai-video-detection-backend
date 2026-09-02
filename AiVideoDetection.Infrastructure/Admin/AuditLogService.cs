using System.Net;
using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Admin.Interfaces;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiVideoDetection.Infrastructure.Admin;

public sealed class AuditLogService(
    AppDbContext dbContext,
    ICorrelationIdAccessor correlationIdAccessor,
    IMonitoringAlertService monitoringAlertService,
    ILogger<AuditLogService> logger) : IAuditLogService
{
    private const int MaxPageSize = 100;

    public async Task LogAsync(
        AuditLogCreateDto entry,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(entry.Action) || string.IsNullOrWhiteSpace(entry.Category))
        {
            return;
        }

        try
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = entry.UserId,
                UserName = Truncate(entry.UserName, 200),
                UserEmail = Truncate(entry.UserEmail, 320),
                Category = Truncate(entry.Category, 80) ?? "General",
                Action = Truncate(entry.Action, 120) ?? "Unknown",
                Severity = Truncate(entry.Severity, 40) ?? "Information",
                Message = Truncate(entry.Message, 1000) ?? string.Empty,
                ResourceType = Truncate(entry.ResourceType, 80),
                ResourceId = Truncate(entry.ResourceId, 120),
                HttpMethod = Truncate(entry.HttpMethod, 20),
                Path = Truncate(entry.Path, 500),
                StatusCode = entry.StatusCode,
                IpAddress = ParseIpAddress(entry.IpAddress),
                UserAgent = Truncate(entry.UserAgent, 500),
                DetailsJson = string.IsNullOrWhiteSpace(entry.DetailsJson) ? null : entry.DetailsJson,
                CorrelationId = Truncate(entry.CorrelationId ?? correlationIdAccessor.CorrelationId, 128)
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            await RecordMonitoringSignalsAsync(entry, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Audit log write failed for action {Action}.", entry.Action);
        }
    }

    public async Task<ApiResponse<PagedResponse<AdminAuditLogDto>>> GetLogsAsync(
        int page,
        int pageSize,
        string? search,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? severity,
        string? category,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = dbContext.AuditLogs
            .AsNoTracking()
            .Where(log => log.Action != "ViewAnalysisResult"
                && log.Action != "AdminVideoReview"
                && log.Action != "AdminVideoPlayback"
                && (log.Path == null || log.Path != "/api/videos/history"));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(log =>
                (log.UserName != null && log.UserName.ToLower().Contains(normalizedSearch)) ||
                (log.UserEmail != null && log.UserEmail.ToLower().Contains(normalizedSearch)));
        }

        if (from is not null)
        {
            query = query.Where(log => log.CreatedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(log => log.CreatedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(log => log.Severity.ToLower() == severity.Trim().ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(log => log.Category.ToLower() == category.Trim().ToLowerInvariant());
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var logs = await query
            .OrderByDescending(log => log.CreatedAt)
            .ThenByDescending(log => log.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(log => new AdminAuditLogDto
            {
                Id = log.Id,
                UserId = log.UserId,
                UserName = log.UserName,
                UserEmail = log.UserEmail,
                Category = log.Category,
                Action = log.Action,
                Severity = log.Severity,
                Message = log.Message,
                ResourceType = log.ResourceType,
                ResourceId = log.ResourceId,
                HttpMethod = log.HttpMethod,
                Path = log.Path,
                StatusCode = log.StatusCode,
                IpAddress = log.IpAddress == null ? null : log.IpAddress.ToString(),
                UserAgent = log.UserAgent,
                DetailsJson = log.DetailsJson,
                CorrelationId = log.CorrelationId,
                CreatedAt = log.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<PagedResponse<AdminAuditLogDto>>.SuccessResponse(new PagedResponse<AdminAuditLogDto>
        {
            Items = logs,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize)
        });
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static IPAddress? ParseIpAddress(string? ipAddress)
    {
        return IPAddress.TryParse(ipAddress, out var parsedIpAddress)
            ? parsedIpAddress
            : null;
    }

    private async Task RecordMonitoringSignalsAsync(AuditLogCreateDto entry, CancellationToken cancellationToken)
    {
        if (entry.Action is "LoginFailed" or "LoginRejected" or "LoginThrottled")
        {
            var partitionKey = entry.UserEmail ?? entry.IpAddress ?? "unknown-login";
            await monitoringAlertService.RecordFailedLoginAsync(partitionKey, entry.UserEmail, entry.IpAddress, cancellationToken);
        }

        if (entry.StatusCode >= 500 || string.Equals(entry.Severity, "Error", StringComparison.OrdinalIgnoreCase))
        {
            var partitionKey = string.IsNullOrWhiteSpace(entry.Path) ? entry.Action : entry.Path;
            await monitoringAlertService.RecordApiErrorAsync(
                partitionKey ?? "unknown-api-error",
                entry.Path,
                entry.StatusCode,
                entry.CorrelationId,
                cancellationToken);
        }
    }
}
