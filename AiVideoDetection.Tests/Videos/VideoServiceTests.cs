using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Application.Videos.Validators;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class VideoServiceTests
{
    [Fact]
    public async Task UploadValidVideoCreatesVideoAndAnalysisJob()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.Add(CreateUser(1, "owner@example.com"));
        await dbContext.SaveChangesAsync();
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage);

        var response = await service.UploadAsync(CreateUploadRequest(), 1, null);

        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(1, await dbContext.Videos.CountAsync());
        Assert.Equal(1, await dbContext.AnalysisJobs.CountAsync());
        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(JobStatus.Queued, (await dbContext.AnalysisJobs.SingleAsync()).Status);
    }

    [Fact]
    public async Task UploadAttemptsStorageCleanupWhenDatabaseSaveFails()
    {
        await using var dbContext = new FailingSaveAppDbContext(CreateOptions());
        var storage = new FakeObjectStorageService();
        var service = CreateService(dbContext, storage);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UploadAsync(CreateUploadRequest(), 1, null));

        Assert.Equal(1, storage.UploadCalls);
        Assert.Equal(1, storage.DeleteCalls);
    }

    [Fact]
    public async Task UserCannotAccessAnotherUsersVideoDetail()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        dbContext.Videos.Add(new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Uploaded
        });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService());

        var response = await service.GetVideoDetailAsync(10, 2);

        Assert.False(response.Success);
    }

    [Fact]
    public async Task UserCannotAccessAnotherUsersJobStatus()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        var video = new Video
        {
            Id = 10,
            UserId = 1,
            OriginalName = "owner.mp4",
            FileUrl = "videos/1/file.mp4",
            FileSize = 100,
            Status = VideoStatus.Uploaded
        };
        dbContext.Videos.Add(video);
        dbContext.AnalysisJobs.Add(new AnalysisJob
        {
            Id = 20,
            Video = video,
            Status = JobStatus.Queued,
            CurrentStep = "Waiting"
        });
        await dbContext.SaveChangesAsync();
        var service = new JobService(dbContext);

        var response = await service.GetStatusByVideoIdAsync(10, 2);

        Assert.False(response.Success);
    }

    [Fact]
    public async Task HistoryOnlyReturnsCurrentUsersVideos()
    {
        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(CreateUser(1, "owner@example.com"), CreateUser(2, "other@example.com"));
        dbContext.Videos.AddRange(
            new Video { UserId = 1, OriginalName = "one.mp4", FileUrl = "one", FileSize = 1, Status = VideoStatus.Uploaded },
            new Video { UserId = 2, OriginalName = "two.mp4", FileUrl = "two", FileSize = 1, Status = VideoStatus.Uploaded },
            new Video { UserId = 1, OriginalName = "deleted.mp4", FileUrl = "deleted", FileSize = 1, Status = VideoStatus.Deleted, DeletedAt = DateTimeOffset.UtcNow });
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext, new FakeObjectStorageService());

        var response = await service.GetHistoryAsync(1, 1, 20, null);

        Assert.True(response.Success);
        Assert.Single(response.Data!.Items);
        Assert.Equal("one.mp4", response.Data.Items[0].OriginalName);
    }

    private static VideoService CreateService(AppDbContext dbContext, IObjectStorageService storage)
    {
        var validator = new UploadVideoRequestValidator(Options.Create(new VideoUploadOptions()));
        return new VideoService(dbContext, storage, validator, NullLogger<VideoService>.Instance);
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(CreateOptions());
    }

    private static DbContextOptions<AppDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
    }

    private static User CreateUser(long id, string email)
    {
        return new User
        {
            Id = id,
            Name = "Test User",
            Email = email,
            PasswordHash = "hash",
            Role = UserRole.User,
            IsActive = true
        };
    }

    private static UploadVideoRequest CreateUploadRequest()
    {
        var stream = new MemoryStream([1, 2, 3]);
        return new UploadVideoRequest
        {
            File = new FormFile(stream, 0, stream.Length, "file", "sample.mp4")
            {
                Headers = new HeaderDictionary(),
                ContentType = "video/mp4"
            },
            ConsentAccepted = true,
            AnalysisMode = AnalysisMode.Basic
        };
    }

    private sealed class FakeObjectStorageService : IObjectStorageService
    {
        public int UploadCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public Task<string> UploadAsync(Stream stream, string objectKey, string contentType, CancellationToken cancellationToken = default)
        {
            UploadCalls++;
            return Task.FromResult(objectKey);
        }

        public Task DeleteAsync(string objectKeyOrUrl, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.CompletedTask;
        }

        public Task<string> GetReadUrlAsync(string objectKeyOrUrl, TimeSpan expiry, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(objectKeyOrUrl);
        }
    }

    private sealed class FailingSaveAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Database save failed.");
        }
    }
}
