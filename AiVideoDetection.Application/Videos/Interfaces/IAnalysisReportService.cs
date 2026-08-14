using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.DTOs;

namespace AiVideoDetection.Application.Videos.Interfaces;

public interface IAnalysisReportService
{
    Task<ApiResponse<AnalysisReportFile>> GeneratePdfAsync(
        long videoId,
        long currentUserId,
        CancellationToken cancellationToken = default);
}
