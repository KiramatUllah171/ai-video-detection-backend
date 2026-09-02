using AiVideoDetection.Application.Common;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Common;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class JobLogServiceTests
{
    [Fact]
    public async Task LogAsyncPersistsExistingCorrelationId()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedJobAsync(dbContext);
        var accessor = new CorrelationIdAccessor { CorrelationId = "job-correlation-id" };
        var service = new JobLogService(dbContext, accessor, CreateAlertService());

        await service.LogAsync(job.Id, "Step", "Information", "Step completed.");

        var log = await dbContext.JobLogs.SingleAsync();
        Assert.Equal("job-correlation-id", log.CorrelationId);
    }

    [Fact]
    public async Task LogAsyncGeneratesCorrelationIdWhenMissing()
    {
        await using var dbContext = CreateDbContext();
        var job = await SeedJobAsync(dbContext);
        var accessor = new CorrelationIdAccessor();
        var service = new JobLogService(dbContext, accessor, CreateAlertService());

        await service.LogAsync(job.Id, "Step", "Information", "Step completed.");

        var log = await dbContext.JobLogs.SingleAsync();
        Assert.False(string.IsNullOrWhiteSpace(log.CorrelationId));
        Assert.Equal(log.CorrelationId, accessor.CorrelationId);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }

    private static LoggingMonitoringAlertService CreateAlertService()
    {
        return new LoggingMonitoringAlertService(
            Options.Create(new MonitoringOptions()),
            NullLogger<LoggingMonitoringAlertService>.Instance);
    }

    private static async Task<AnalysisJob> SeedJobAsync(AppDbContext dbContext)
    {
        var user = new User
        {
            Name = "Test User",
            Email = "test@example.com",
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true,
            EmailConfirmed = true
        };

        var video = new Video
        {
            User = user,
            OriginalName = "test.mp4",
            FileUrl = "videos/test.mp4",
            ContentType = "video/mp4",
            FileSize = 1024,
            Status = VideoStatus.Uploaded
        };

        var job = new AnalysisJob
        {
            Video = video,
            Status = JobStatus.Queued,
            Progress = 0,
            CurrentStep = "Queued"
        };

        dbContext.AnalysisJobs.Add(job);
        await dbContext.SaveChangesAsync();
        return job;
    }
}
