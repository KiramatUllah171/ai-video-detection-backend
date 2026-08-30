using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Admin;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiVideoDetection.Tests.Admin;

public class AuditLogServiceTests
{
    [Fact]
    public async Task GetLogsAsyncFiltersByUserDateSeverityAndCategory()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext, NullLogger<AuditLogService>.Instance);
        var now = DateTimeOffset.UtcNow;

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = 1,
            UserName = "Test User",
            UserEmail = "test-user@example.com",
            Category = "Report",
            Action = "ReportDownload",
            Severity = "Information",
            Message = "ReportDownload completed.",
            CreatedAt = now.AddMinutes(-10)
        });

        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = 2,
            UserName = "Other User",
            UserEmail = "other-user@example.com",
            Category = "Video",
            Action = "UploadVideo",
            Severity = "Warning",
            Message = "UploadVideo failed with HTTP 400.",
            CreatedAt = now.AddDays(-3)
        });
        await dbContext.SaveChangesAsync();

        var response = await service.GetLogsAsync(
            page: 1,
            pageSize: 10,
            search: "test-user",
            from: now.AddHours(-1),
            to: now.AddHours(1),
            severity: "Information",
            category: "Report");

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        var log = Assert.Single(response.Data.Items);
        Assert.Equal("Test User", log.UserName);
        Assert.Equal("ReportDownload", log.Action);
        Assert.Equal(1, response.Data.TotalCount);
    }

    [Fact]
    public async Task GetLogsAsyncReturnsTenItemsPerPageByDefaultRequest()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext, NullLogger<AuditLogService>.Instance);

        for (var index = 0; index < 12; index++)
        {
            await service.LogAsync(new AuditLogCreateDto
            {
                UserId = 1,
                UserName = "Test User",
                UserEmail = "test-user@example.com",
                Category = "Video",
                Action = "UploadVideo",
                Severity = "Information",
                Message = "UploadVideo completed."
            });
        }

        var response = await service.GetLogsAsync(
            page: 1,
            pageSize: 10,
            search: null,
            from: null,
            to: null,
            severity: null,
            category: null);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(10, response.Data.Items.Count);
        Assert.Equal(12, response.Data.TotalCount);
        Assert.Equal(2, response.Data.TotalPages);
    }

    [Fact]
    public async Task GetLogsAsyncExcludesNoisyReadOnlyLogs()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext, NullLogger<AuditLogService>.Instance);
        var now = DateTimeOffset.UtcNow;

        dbContext.AuditLogs.AddRange(
            new AuditLog
            {
                UserName = "Test User",
                Category = "Video",
                Action = "ViewAnalysisResult",
                Severity = "Information",
                Message = "ViewAnalysisResult completed.",
                Path = "/api/videos/1/analysis",
                CreatedAt = now
            },
            new AuditLog
            {
                UserName = "Test User",
                Category = "Video",
                Action = "GET /api/videos/history",
                Severity = "Error",
                Message = "Unhandled API error occurred.",
                Path = "/api/videos/history",
                CreatedAt = now
            },
            new AuditLog
            {
                UserName = "Test User",
                Category = "Admin",
                Action = "AdminVideoReview",
                Severity = "Information",
                Message = "AdminVideoReview completed.",
                Path = "/api/admin/videos/1",
                CreatedAt = now
            },
            new AuditLog
            {
                UserName = "Test User",
                Category = "Admin",
                Action = "AdminVideoPlayback",
                Severity = "Information",
                Message = "AdminVideoPlayback completed.",
                Path = "/api/admin/videos/1/file",
                CreatedAt = now
            },
            new AuditLog
            {
                UserName = "Test User",
                Category = "Report",
                Action = "ReportDownload",
                Severity = "Information",
                Message = "ReportDownload completed.",
                Path = "/api/videos/1/report/pdf",
                CreatedAt = now
            });
        await dbContext.SaveChangesAsync();

        var response = await service.GetLogsAsync(
            page: 1,
            pageSize: 10,
            search: null,
            from: null,
            to: null,
            severity: null,
            category: null);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        var log = Assert.Single(response.Data.Items);
        Assert.Equal("ReportDownload", log.Action);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }
}
