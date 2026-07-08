using System.Net;
using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Api.Middleware;

public class GlobalExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception occurred.");

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var response = ApiResponse<object>.ErrorResponse(
                "An unexpected error occurred.",
                ["Please try again later."]);

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
