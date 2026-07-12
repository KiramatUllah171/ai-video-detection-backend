using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;
using AiVideoDetection.Application.Videos.Matching;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IInternalVideoMatchingService
{
    Task<IReadOnlyList<InternalVideoMatchResult>> MatchVideoAsync(
        long videoId,
        CancellationToken cancellationToken = default);

    Task<ApiResponse<IReadOnlyList<SourceMatchDto>>> GetMatchesAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);
}
