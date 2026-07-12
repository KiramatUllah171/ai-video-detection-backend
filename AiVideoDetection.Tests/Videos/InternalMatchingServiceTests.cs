using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Matching;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Videos.Matching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Tests.Videos;

public class InternalMatchingServiceTests
{
    [Fact]
    public async Task PlaceholderProviderReturnsDeterministicFixedLengthHashes()
    {
        var provider = new PlaceholderPerceptualHashProvider();
        var input = new FrameHashInput(1, 10, "frames/10/frame_000001.jpg", 1, 2, "mvp-v1");

        var first = await provider.GenerateAsync(input);
        var second = await provider.GenerateAsync(input);
        var different = await provider.GenerateAsync(input with { FrameIndex = 2, FrameUrl = "frames/10/frame_000002.jpg" });

        Assert.Equal(first, second);
        Assert.NotEqual(first.PHash, different.PHash);
        Assert.Equal(16, first.PHash.Length);
        Assert.Equal(16, first.DHash.Length);
        Assert.Equal(16, first.AHash.Length);
    }

    [Fact]
    public async Task PlaceholderProviderUsesVideoHashForStableSameUploadMatching()
    {
        var provider = new PlaceholderPerceptualHashProvider();

        var first = await provider.GenerateAsync(new FrameHashInput(
            1,
            10,
            "frames/10/frame_000001.jpg",
            1,
            0,
            "mvp-v1",
            "same-video-sha"));
        var second = await provider.GenerateAsync(new FrameHashInput(
            2,
            11,
            "frames/11/frame_000001.jpg",
            1,
            0,
            "mvp-v1",
            "same-video-sha"));

        Assert.Equal(first, second);
    }

    [Fact]
    public void HammingDistanceHandlesHexAndSimilarity()
    {
        var service = new HammingDistanceService();

        var identical = service.Calculate("0000000000000000", "0000000000000000");
        var different = service.Calculate("0000000000000000", "ffffffffffffffff");

        Assert.Equal(0, identical);
        Assert.True(different > 0);
        Assert.Equal(1m, service.ToSimilarity(identical, 16));
        Assert.InRange(service.ToSimilarity(different, 16), 0m, 1m);
        Assert.Equal(int.MaxValue, service.Calculate("bad", "0000"));
    }

    [Fact]
    public async Task FrameHashServiceGeneratesHashesAndDoesNotDuplicate()
    {
        await using var dbContext = CreateDbContext();
        await SeedVideoWithFramesAsync(dbContext, videoId: 10, userId: 1, "same-a", "same-b");
        var service = CreateFrameHashService(dbContext);

        var first = await service.GenerateHashesForVideoAsync(10);
        var second = await service.GenerateHashesForVideoAsync(10);

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(2, await dbContext.FrameHashes.CountAsync());
    }

    [Fact]
    public async Task InternalMatchingCreatesInternalSourceMatchAndReplacesOnRerun()
    {
        await using var dbContext = CreateDbContext();
        await SeedVideoWithFramesAsync(dbContext, videoId: 10, userId: 1, "same-a", "same-b");
        await SeedVideoWithFramesAsync(dbContext, videoId: 11, userId: 2, "same-a", "same-b");
        await CreateFrameHashService(dbContext).GenerateHashesForVideoAsync(11);
        var service = CreateMatchingService(dbContext);

        var first = await service.MatchVideoAsync(10);
        var second = await service.MatchVideoAsync(10);
        var dtoResponse = await service.GetMatchesAsync(10, 1);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(1, await dbContext.SourceMatches.CountAsync(match => match.VideoId == 10 && match.Platform == "Internal"));
        Assert.True(first[0].SimilarityScore >= 0.55m);
        Assert.True(dtoResponse.Success);
        var dto = Assert.Single(dtoResponse.Data!);
        Assert.Equal("Internal", dto.Platform);
        Assert.Equal("Previously analyzed internal video", dto.Title);
        Assert.DoesNotContain("uploader", dto.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InternalMatchingDoesNotMatchWhenHashesAreTooDifferent()
    {
        await using var dbContext = CreateDbContext();
        await SeedVideoWithFramesAsync(dbContext, videoId: 10, userId: 1, "current-a", "current-b");
        await SeedVideoWithFramesAsync(dbContext, videoId: 11, userId: 2, "candidate-x", "candidate-y");
        await CreateFrameHashService(dbContext).GenerateHashesForVideoAsync(11);
        var service = CreateMatchingService(dbContext, new InternalMatchingOptions
        {
            HashVersion = "mvp-v1",
            HammingDistanceThreshold = 0,
            MinimumMatchedFrames = 2,
            MinimumSimilarityScore = 0.99m,
            MaxCandidateVideos = 200,
            MaxFramesPerVideoToCompare = 30,
            MaxMatchesToStore = 10,
            HashSimilarityWeight = 0.70m,
            DurationSimilarityWeight = 0.20m,
            MetadataSimilarityWeight = 0.10m
        });

        var matches = await service.MatchVideoAsync(10);

        Assert.Empty(matches);
        Assert.Empty(await dbContext.SourceMatches.Where(match => match.VideoId == 10).ToListAsync());
    }

    [Fact]
    public async Task GetMatchesEnforcesOwnership()
    {
        await using var dbContext = CreateDbContext();
        await SeedVideoWithFramesAsync(dbContext, videoId: 10, userId: 1, "same-a");
        var service = CreateMatchingService(dbContext);

        var response = await service.GetMatchesAsync(10, currentUserId: 2);

        Assert.False(response.Success);
    }

    private static FrameHashService CreateFrameHashService(AppDbContext dbContext)
    {
        return new FrameHashService(
            dbContext,
            new PlaceholderPerceptualHashProvider(),
            Options.Create(DefaultOptions()),
            NullLogger<FrameHashService>.Instance);
    }

    private static InternalVideoMatchingService CreateMatchingService(
        AppDbContext dbContext,
        InternalMatchingOptions? options = null)
    {
        return new InternalVideoMatchingService(
            dbContext,
            CreateFrameHashService(dbContext),
            new HammingDistanceService(),
            Options.Create(options ?? DefaultOptions()),
            NullLogger<InternalVideoMatchingService>.Instance);
    }

    private static InternalMatchingOptions DefaultOptions()
    {
        return new InternalMatchingOptions
        {
            Enabled = true,
            HashVersion = "mvp-v1",
            MaxCandidateVideos = 200,
            MaxFramesPerVideoToCompare = 30,
            MaxMatchesToStore = 10,
            HammingDistanceThreshold = 18,
            MinimumMatchedFrames = 2,
            MinimumSimilarityScore = 0.55m,
            HighConfidenceThreshold = 0.80m,
            MediumConfidenceThreshold = 0.65m,
            HashSimilarityWeight = 0.70m,
            DurationSimilarityWeight = 0.20m,
            MetadataSimilarityWeight = 0.10m
        };
    }

    private static async Task SeedVideoWithFramesAsync(
        AppDbContext dbContext,
        long videoId,
        long userId,
        params string[] frameUrls)
    {
        var user = await dbContext.Users.FindAsync(userId);
        if (user is null)
        {
            user = new User
            {
                Id = userId,
                Name = $"User {userId}",
                Email = $"user{userId}@example.com",
                PasswordHash = "hash",
                Role = UserRole.User,
                IsActive = true
            };
            dbContext.Users.Add(user);
        }

        var video = new Video
        {
            Id = videoId,
            User = user,
            OriginalName = $"video-{videoId}.mp4",
            FileUrl = $"videos/{userId}/{videoId}.mp4",
            FileExtension = ".mp4",
            FileSize = 100,
            Sha256Hash = frameUrls.Length > 0 && frameUrls[0].StartsWith("same", StringComparison.OrdinalIgnoreCase)
                ? "same-video-sha"
                : $"sha-{videoId}",
            DurationSeconds = 10,
            FormatName = "mov,mp4",
            Status = VideoStatus.Completed,
            MetadataResult = new MetadataResult
            {
                Codec = "h264",
                Resolution = "1920x1080",
                RawJson = "{}",
                WarningsJson = "[]"
            }
        };

        for (var i = 0; i < frameUrls.Length; i++)
        {
            video.Frames.Add(new VideoFrame
            {
                FrameUrl = frameUrls[i],
                FrameIndex = i + 1,
                TimestampSeconds = i * 2
            });
        }

        dbContext.Videos.Add(video);
        await dbContext.SaveChangesAsync();
    }

    private static AppDbContext CreateDbContext()
    {
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
    }
}
