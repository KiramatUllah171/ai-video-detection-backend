using AiVideoDetection.Application.Videos.Ai;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IAiInferenceClient
{
    Task<AiAnalyzeFramesResponse> AnalyzeFramesAsync(
        AiAnalyzeFramesRequest request,
        CancellationToken cancellationToken = default);

    Task<AiAnalyzeFramesResponse> AnalyzeVideoAsync(
        AiAnalyzeVideoRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> HealthCheckAsync(CancellationToken cancellationToken = default);
}
