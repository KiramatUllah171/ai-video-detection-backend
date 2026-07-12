using System.Text.Json;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Matching;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Infrastructure.Videos.Matching;

public class InternalVideoMatchingService(
    AppDbContext dbContext,
    IFrameHashService frameHashService,
    IHammingDistanceService hammingDistanceService,
    IOptions<InternalMatchingOptions> options,
    ILogger<InternalVideoMatchingService> logger) : IInternalVideoMatchingService
{
    private const string InternalPlatform = "Internal";
    private const string SafeInternalTitle = "Previously analyzed internal video";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly InternalMatchingOptions _options = options.Value;

    public async Task<IReadOnlyList<InternalVideoMatchResult>> MatchVideoAsync(
        long videoId,
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Internal matching is disabled. Skipping video {VideoId}.", videoId);
            await ReplaceInternalMatchesAsync(videoId, [], cancellationToken);
            return [];
        }

        var currentVideo = await dbContext.Videos
            .AsNoTracking()
            .Include(video => video.MetadataResult)
            .FirstOrDefaultAsync(
                video => video.Id == videoId && video.DeletedAt == null && video.Status != VideoStatus.Deleted,
                cancellationToken);

        if (currentVideo is null)
        {
            return [];
        }

        await frameHashService.GenerateHashesForVideoAsync(videoId, cancellationToken);
        var currentHashes = await LoadHashesForVideoAsync(videoId, cancellationToken);
        if (currentHashes.Count == 0)
        {
            await ReplaceInternalMatchesAsync(videoId, [], cancellationToken);
            return [];
        }

        var candidateVideos = await dbContext.Videos
            .AsNoTracking()
            .Include(video => video.MetadataResult)
            .Where(video => video.Id != videoId
                && video.DeletedAt == null
                && video.Status == VideoStatus.Completed
                && video.FrameHashes.Any(hash => hash.HashVersion == _options.HashVersion))
            .OrderByDescending(video => video.CreatedAt)
            .Take(_options.MaxCandidateVideos)
            .ToListAsync(cancellationToken);

        var results = new List<InternalVideoMatchResult>();
        foreach (var candidate in candidateVideos)
        {
            var candidateHashes = await LoadHashesForVideoAsync(candidate.Id, cancellationToken);
            var match = CalculateMatch(currentVideo, currentHashes, candidate, candidateHashes);
            if (match is not null
                && match.SimilarityScore >= _options.MinimumSimilarityScore
                && match.MatchedFrameCount >= _options.MinimumMatchedFrames)
            {
                results.Add(match);
            }
        }

        var ranked = results
            .OrderByDescending(match => match.SimilarityScore)
            .ThenByDescending(match => match.HashMatchScore)
            .ThenBy(match => match.Details.TryGetValue("matchedVideoCreatedAt", out var value) ? value?.ToString() : null)
            .Take(_options.MaxMatchesToStore)
            .Select((match, index) => match with { Rank = index + 1 })
            .ToList();

        await ReplaceInternalMatchesAsync(videoId, ranked, cancellationToken);
        return ranked;
    }

    public async Task<ApiResponse<IReadOnlyList<SourceMatchDto>>> GetMatchesAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default)
    {
        var ownsVideo = await dbContext.Videos
            .AsNoTracking()
            .AnyAsync(video => video.Id == videoId
                && video.UserId == currentUserId
                && video.DeletedAt == null
                && video.Status != VideoStatus.Deleted,
                cancellationToken);

        if (!ownsVideo)
        {
            return ApiResponse<IReadOnlyList<SourceMatchDto>>.ErrorResponse("Origin matches were not found.");
        }

        var matches = await dbContext.SourceMatches
            .AsNoTracking()
            .Where(match => match.VideoId == videoId)
            .OrderBy(match => match.Rank)
            .Select(match => new SourceMatchDto
            {
                Id = match.Id,
                VideoId = match.VideoId,
                Platform = match.Platform,
                Title = match.Platform == InternalPlatform ? SafeInternalTitle : match.Title,
                UploadDatetime = match.UploadDatetime,
                SimilarityScore = match.SimilarityScore,
                DurationMatchScore = match.DurationMatchScore,
                HashMatchScore = match.HashMatchScore,
                MetadataMatchScore = match.MetadataMatchScore,
                Rank = match.Rank,
                Confidence = match.Confidence.ToString(),
                Details = SanitizeDetails(match.DetailsJson)
            })
            .ToListAsync(cancellationToken);

        return ApiResponse<IReadOnlyList<SourceMatchDto>>.SuccessResponse(matches);
    }

    private async Task<List<FrameHash>> LoadHashesForVideoAsync(long videoId, CancellationToken cancellationToken)
    {
        return await dbContext.FrameHashes
            .AsNoTracking()
            .Include(hash => hash.VideoFrame)
            .Where(hash => hash.VideoId == videoId && hash.HashVersion == _options.HashVersion)
            .OrderBy(hash => hash.VideoFrame.FrameIndex)
            .Take(_options.MaxFramesPerVideoToCompare)
            .ToListAsync(cancellationToken);
    }

    private InternalVideoMatchResult? CalculateMatch(
        Video currentVideo,
        IReadOnlyList<FrameHash> currentHashes,
        Video candidateVideo,
        IReadOnlyList<FrameHash> candidateHashes)
    {
        if (candidateHashes.Count == 0)
        {
            return null;
        }

        var bestDistances = new List<int>();
        var bestSimilarities = new List<decimal>();

        foreach (var currentHash in currentHashes)
        {
            var bestDistance = candidateHashes
                .Select(candidateHash => hammingDistanceService.Calculate(currentHash.PHash, candidateHash.PHash))
                .DefaultIfEmpty(int.MaxValue)
                .Min();

            if (bestDistance <= _options.HammingDistanceThreshold)
            {
                bestDistances.Add(bestDistance);
                bestSimilarities.Add(hammingDistanceService.ToSimilarity(bestDistance, currentHash.PHash?.Length ?? 0));
            }
        }

        if (bestDistances.Count == 0)
        {
            return null;
        }

        var averageSimilarity = bestSimilarities.Average();
        var frameCoverage = Math.Clamp(bestDistances.Count / (decimal)Math.Max(currentHashes.Count, 1), 0m, 1m);
        var hashMatchScore = Clamp01((averageSimilarity * 0.80m) + (frameCoverage * 0.20m));
        var durationMatchScore = CalculateDurationSimilarity(currentVideo.DurationSeconds, candidateVideo.DurationSeconds);
        var metadataMatchScore = CalculateMetadataSimilarity(currentVideo, candidateVideo);
        var similarityScore = CalculateWeightedScore(hashMatchScore, durationMatchScore, metadataMatchScore);
        var confidence = ToConfidence(similarityScore);

        var details = new Dictionary<string, object?>
        {
            ["matchedVideoId"] = candidateVideo.Id,
            ["matchedFrameCount"] = bestDistances.Count,
            ["bestDistance"] = bestDistances.Min(),
            ["hashVersion"] = _options.HashVersion,
            ["matchedVideoCreatedAt"] = candidateVideo.CreatedAt,
            ["privacyNote"] = "Private user information is hidden."
        };

        return new InternalVideoMatchResult(
            candidateVideo.Id,
            hashMatchScore,
            durationMatchScore,
            metadataMatchScore,
            similarityScore,
            confidence,
            bestDistances.Count,
            bestDistances.Min(),
            0,
            details);
    }

    private decimal? CalculateDurationSimilarity(decimal? currentDuration, decimal? candidateDuration)
    {
        if (!currentDuration.HasValue || !candidateDuration.HasValue || currentDuration <= 0 || candidateDuration <= 0)
        {
            return null;
        }

        var max = Math.Max(currentDuration.Value, candidateDuration.Value);
        return Clamp01(1m - (Math.Abs(currentDuration.Value - candidateDuration.Value) / max));
    }

    private static decimal? CalculateMetadataSimilarity(Video currentVideo, Video candidateVideo)
    {
        var comparisons = new List<decimal>();

        if (!string.IsNullOrWhiteSpace(currentVideo.FormatName) && !string.IsNullOrWhiteSpace(candidateVideo.FormatName))
        {
            comparisons.Add(string.Equals(currentVideo.FormatName, candidateVideo.FormatName, StringComparison.OrdinalIgnoreCase) ? 1m : 0m);
        }

        if (!string.IsNullOrWhiteSpace(currentVideo.MetadataResult?.Codec) && !string.IsNullOrWhiteSpace(candidateVideo.MetadataResult?.Codec))
        {
            comparisons.Add(string.Equals(currentVideo.MetadataResult.Codec, candidateVideo.MetadataResult.Codec, StringComparison.OrdinalIgnoreCase) ? 1m : 0m);
        }

        if (!string.IsNullOrWhiteSpace(currentVideo.MetadataResult?.Resolution) && !string.IsNullOrWhiteSpace(candidateVideo.MetadataResult?.Resolution))
        {
            comparisons.Add(string.Equals(currentVideo.MetadataResult.Resolution, candidateVideo.MetadataResult.Resolution, StringComparison.OrdinalIgnoreCase) ? 1m : 0m);
        }

        return comparisons.Count == 0 ? null : Clamp01(comparisons.Average());
    }

    private decimal CalculateWeightedScore(decimal hashMatchScore, decimal? durationMatchScore, decimal? metadataMatchScore)
    {
        var weighted = hashMatchScore * _options.HashSimilarityWeight;
        var totalWeight = _options.HashSimilarityWeight;

        if (durationMatchScore.HasValue)
        {
            weighted += durationMatchScore.Value * _options.DurationSimilarityWeight;
            totalWeight += _options.DurationSimilarityWeight;
        }

        if (metadataMatchScore.HasValue)
        {
            weighted += metadataMatchScore.Value * _options.MetadataSimilarityWeight;
            totalWeight += _options.MetadataSimilarityWeight;
        }

        return totalWeight <= 0 ? 0 : Clamp01(weighted / totalWeight);
    }

    private ConfidenceLevel ToConfidence(decimal similarity)
    {
        if (similarity >= _options.HighConfidenceThreshold)
        {
            return ConfidenceLevel.High;
        }

        return similarity >= _options.MediumConfidenceThreshold
            ? ConfidenceLevel.Medium
            : ConfidenceLevel.Low;
    }

    private async Task ReplaceInternalMatchesAsync(
        long videoId,
        IReadOnlyList<InternalVideoMatchResult> matches,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.SourceMatches
            .Where(match => match.VideoId == videoId && match.Platform == InternalPlatform)
            .ToListAsync(cancellationToken);
        dbContext.SourceMatches.RemoveRange(existing);

        foreach (var match in matches)
        {
            dbContext.SourceMatches.Add(new SourceMatch
            {
                VideoId = videoId,
                Platform = InternalPlatform,
                Url = null,
                Title = SafeInternalTitle,
                UploaderName = null,
                UploadDatetime = match.Details.TryGetValue("matchedVideoCreatedAt", out var createdAt)
                    && createdAt is DateTimeOffset timestamp
                        ? timestamp
                        : null,
                SimilarityScore = match.SimilarityScore,
                DurationMatchScore = match.DurationMatchScore,
                HashMatchScore = match.HashMatchScore,
                MetadataMatchScore = match.MetadataMatchScore,
                SourceCredibilityScore = 0.70m,
                Rank = match.Rank,
                Confidence = match.Confidence,
                DetailsJson = JsonSerializer.Serialize(match.Details, SerializerOptions)
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string? SanitizeDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson))
        {
            return detailsJson;
        }

        try
        {
            using var document = JsonDocument.Parse(detailsJson);
            var root = document.RootElement;
            var sanitized = new Dictionary<string, object?>
            {
                ["matchedVideoId"] = root.TryGetProperty("matchedVideoId", out var matchedVideoId) ? matchedVideoId.GetInt64() : null,
                ["matchedFrameCount"] = root.TryGetProperty("matchedFrameCount", out var count) ? count.GetInt32() : null,
                ["bestDistance"] = root.TryGetProperty("bestDistance", out var distance) ? distance.GetInt32() : null,
                ["hashVersion"] = root.TryGetProperty("hashVersion", out var version) ? version.GetString() : null,
                ["privacyNote"] = "Private user information is hidden."
            };

            return JsonSerializer.Serialize(sanitized, SerializerOptions);
        }
        catch
        {
            return "{\"privacyNote\":\"Private user information is hidden.\"}";
        }
    }

    private static decimal Clamp01(decimal value)
    {
        return Math.Clamp(value, 0m, 1m);
    }
}
