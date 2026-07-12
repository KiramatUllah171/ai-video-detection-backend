using System.Text.Json;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiVideoDetection.Api.Controllers;

[ApiController]
[Authorize(Roles = "Admin,EnterpriseAdmin")]
[Route("api/dev/analysis-debug")]
public class DevAnalysisDebugController(
    AppDbContext dbContext,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("{videoId:long}")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<object>>> Get(long videoId, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound(ApiResponse<object>.ErrorResponse("Endpoint is available only in Development."));
        }

        var result = await dbContext.AiResults
            .AsNoTracking()
            .Include(aiResult => aiResult.ModelVersion)
            .Include(aiResult => aiResult.EvidenceItems)
            .Where(aiResult => aiResult.VideoId == videoId)
            .OrderByDescending(aiResult => aiResult.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return NotFound(ApiResponse<object>.ErrorResponse("Analysis result is not available yet."));
        }

        var rawModelOutput = ParseJson(result.RawModelOutputJson);
        var providerRequests = await dbContext.AiProviderRequests
            .AsNoTracking()
            .Where(request => request.VideoId == videoId)
            .OrderByDescending(request => request.CreatedAt)
            .Take(20)
            .Select(request => new
            {
                request.Id,
                request.ProviderName,
                request.ProviderMode,
                request.ProviderRequestId,
                request.ProviderJobId,
                request.Status,
                request.RequestStartedAt,
                request.RequestCompletedAt,
                request.DurationMs,
                request.HttpStatusCode,
                request.ErrorMessage,
                rawRequestMetadata = request.RawRequestMetadataJson == null ? null : ParseJson(request.RawRequestMetadataJson),
                rawResponse = request.RawResponseJson == null ? null : ParseJson(request.RawResponseJson)
            })
            .ToListAsync(cancellationToken);
        var quotaUsage = await dbContext.ApiUsageMonthly
            .AsNoTracking()
            .OrderByDescending(usage => usage.Year)
            .ThenByDescending(usage => usage.Month)
            .Take(12)
            .ToListAsync(cancellationToken);
        return Ok(ApiResponse<object>.SuccessResponse(new
        {
            aiResultId = result.Id,
            result.VideoId,
            modelVersion = result.ModelVersion?.Version,
            result.VisualScore,
            result.MetadataScore,
            result.TemporalScore,
            result.FinalScore,
            result.Confidence,
            label = result.Label.ToString(),
            result.Summary,
            result.CreatedAt,
            provider = result.Provider,
            providerMode = result.ProviderMode,
            finalDecisionSource = result.FinalDecisionSource,
            externalProviderName = result.ExternalProviderName,
            externalProviderStatus = result.ExternalProviderStatus,
            externalScore = result.ExternalScore,
            externalConfidence = result.ExternalConfidence,
            externalLabel = result.ExternalLabel,
            externalErrorMessage = result.ExternalErrorMessage,
            externalRawResponse = result.ExternalRawResponseJson == null ? null : ParseJson(result.ExternalRawResponseJson),
            localResult = result.LocalResultJson == null ? null : ParseJson(result.LocalResultJson),
            hybridResult = result.HybridResultJson == null ? null : ParseJson(result.HybridResultJson),
            fallbackUsed = result.FallbackUsed,
            result.FallbackReason,
            evidenceCount = result.EvidenceItems.Count,
            rawModelOutput,
            providerRequests,
            quotaUsage
        }));
    }

    private static object ParseJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return json;
        }
    }
}
