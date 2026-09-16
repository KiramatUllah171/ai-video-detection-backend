using AiVideoDetection.Api.Options;
using AiVideoDetection.Application.Common;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Api.Middleware;

public sealed class RequestBodySizeLimitMiddleware(
    RequestDelegate next,
    IOptions<RequestLimitOptions> options)
{
    private const string UploadPath = "/api/videos/upload";
    private const string GuestUploadPath = "/api/videos/guest-upload";
    private readonly RequestLimitOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor correlationIdAccessor)
    {
        var limit = GetLimit(context);
        var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (maxRequestBodySizeFeature is { IsReadOnly: false })
        {
            maxRequestBodySizeFeature.MaxRequestBodySize = limit;
        }

        var contentLength = context.Request.ContentLength;
        if (contentLength is not null && contentLength > limit)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/json";

            await context.Response.WriteAsJsonAsync(
                ApiResponse<object>.ErrorResponse(
                    "Request body is too large.",
                    correlationId: correlationIdAccessor.CorrelationId ?? context.TraceIdentifier));
            return;
        }

        await next(context);
    }

    private long GetLimit(HttpContext context)
    {
        return IsUploadRequest(context)
            ? _options.MaxUploadBodySizeBytes
            : _options.MaxApiBodySizeBytes;
    }

    private static bool IsUploadRequest(HttpContext context)
    {
        return context.Request.Path.StartsWithSegments(UploadPath, StringComparison.OrdinalIgnoreCase) ||
            context.Request.Path.StartsWithSegments(GuestUploadPath, StringComparison.OrdinalIgnoreCase);
    }
}
