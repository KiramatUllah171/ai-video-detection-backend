using AiVideoDetection.Domain.Enums;

namespace AiVideoDetection.Application.Videos.Matching;

public sealed record InternalVideoMatchResult(
    long MatchedVideoId,
    decimal HashMatchScore,
    decimal? DurationMatchScore,
    decimal? MetadataMatchScore,
    decimal SimilarityScore,
    ConfidenceLevel Confidence,
    int MatchedFrameCount,
    int BestDistance,
    int Rank,
    IReadOnlyDictionary<string, object?> Details);
