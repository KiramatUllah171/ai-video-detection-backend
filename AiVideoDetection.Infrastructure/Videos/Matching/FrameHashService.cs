using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Matching;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Matching;

public class FrameHashService(
    AppDbContext dbContext,
    IPerceptualHashProvider hashProvider,
    IOptions<InternalMatchingOptions> options,
    ILogger<FrameHashService> logger) : IFrameHashService
{
    private readonly InternalMatchingOptions _options = options.Value;

    public async Task<IReadOnlyList<FrameHashResult>> GenerateHashesForVideoAsync(
        long videoId,
        CancellationToken cancellationToken = default)
    {
        var frames = await dbContext.VideoFrames
            .Include(frame => frame.Video)
            .Where(frame => frame.VideoId == videoId)
            .OrderBy(frame => frame.FrameIndex)
            .Take(_options.MaxFramesPerVideoToCompare)
            .ToListAsync(cancellationToken);

        if (frames.Count == 0)
        {
            logger.LogWarning("No extracted frames were found for video {VideoId}; frame hashes were not generated.", videoId);
            return [];
        }

        var results = new List<FrameHashResult>(frames.Count);
        foreach (var frame in frames)
        {
            results.Add(await GenerateHashForFrameAsync(frame, cancellationToken));
        }

        return results;
    }

    public async Task<FrameHashResult> GenerateHashForFrameAsync(
        VideoFrame frame,
        CancellationToken cancellationToken = default)
    {
        var hashVersion = string.IsNullOrWhiteSpace(_options.HashVersion) ? "mvp-v1" : _options.HashVersion;
        var existing = await dbContext.FrameHashes
            .FirstOrDefaultAsync(
                hash => hash.FrameId == frame.Id && hash.HashVersion == hashVersion,
                cancellationToken);

        if (existing is not null)
        {
            return Map(existing);
        }

        var generated = await hashProvider.GenerateAsync(new FrameHashInput(
            frame.Id,
            frame.VideoId,
            frame.FrameUrl,
            frame.FrameIndex,
            frame.TimestampSeconds,
            hashVersion,
            frame.Video?.Sha256Hash), cancellationToken);

        var frameHash = new FrameHash
        {
            VideoId = frame.VideoId,
            FrameId = frame.Id,
            PHash = generated.PHash,
            DHash = generated.DHash,
            AHash = generated.AHash,
            HashVersion = hashVersion
        };

        dbContext.FrameHashes.Add(frameHash);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Map(frameHash);
    }

    private static FrameHashResult Map(FrameHash hash)
    {
        return new FrameHashResult(
            hash.FrameId,
            hash.VideoId,
            hash.PHash,
            hash.DHash,
            hash.AHash,
            hash.HashVersion);
    }
}
