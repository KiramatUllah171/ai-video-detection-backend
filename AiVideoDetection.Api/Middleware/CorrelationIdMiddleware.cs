using System.Text.RegularExpressions;
using AiVideoDetection.Application.Common;

namespace AiVideoDetection.Api.Middleware;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    private const int MaxCorrelationIdLength = 128;

    public async Task InvokeAsync(HttpContext context, ICorrelationIdAccessor correlationIdAccessor)
    {
        var correlationId = ResolveCorrelationId(context.Request);
        correlationIdAccessor.CorrelationId = correlationId;
        context.TraceIdentifier = correlationId;

        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        await next(context);
    }

    private static string ResolveCorrelationId(HttpRequest request)
    {
        var headerValue = request.Headers[HeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(headerValue))
        {
            var candidate = headerValue.Trim();
            if (candidate.Length <= MaxCorrelationIdLength && CorrelationIdPattern().IsMatch(candidate))
            {
                return candidate;
            }
        }

        return Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}$")]
    private static partial Regex CorrelationIdPattern();
}
