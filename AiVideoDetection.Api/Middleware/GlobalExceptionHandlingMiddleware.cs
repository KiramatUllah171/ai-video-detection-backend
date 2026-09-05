using System.Net;
using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Api.Middleware;

public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor correlationIdAccessor)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation(
                "Request was cancelled by the client. CorrelationId: {CorrelationId}",
                correlationIdAccessor.CorrelationId ?? context.TraceIdentifier);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 499;
            }
        }
        catch (Exception exception)
        {
            var correlationId = correlationIdAccessor.CorrelationId ?? context.TraceIdentifier;
            logger.LogError(exception, "Unhandled exception occurred. CorrelationId: {CorrelationId}", correlationId);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            context.Response.Headers[CorrelationIdMiddleware.HeaderName] = correlationId;

            var response = ApiResponse<object>.ErrorResponse(
                "An unexpected error occurred.",
                ["Please try again later."],
                correlationId);

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
