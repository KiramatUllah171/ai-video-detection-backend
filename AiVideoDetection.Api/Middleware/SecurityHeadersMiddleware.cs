using AiVideoDetection.Api.Options;
using Microsoft.Extensions.Options;

namespace AiVideoDetection.Api.Middleware;

public sealed class SecurityHeadersMiddleware(
    RequestDelegate next,
    IOptions<SecurityHeadersOptions> options,
    IWebHostEnvironment environment)
{
    private readonly SecurityHeadersOptions _options = options.Value;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (!_options.Enabled)
            {
                return Task.CompletedTask;
            }

            SetHeader(context, "X-Content-Type-Options", "nosniff");
            SetHeader(context, "X-Frame-Options", "DENY");
            SetHeader(context, "Referrer-Policy", "no-referrer");
            SetHeader(context, "X-Permitted-Cross-Domain-Policies", "none");
            SetHeader(context, "Cross-Origin-Opener-Policy", "same-origin");
            SetHeader(context, "Cross-Origin-Resource-Policy", "same-site");
            SetHeader(context, "Permissions-Policy", _options.PermissionsPolicy);

            if (!string.IsNullOrWhiteSpace(_options.ContentSecurityPolicy))
            {
                SetHeader(context, "Content-Security-Policy", _options.ContentSecurityPolicy);
            }

            if (_options.EnableHsts && !environment.IsDevelopment() && context.Request.IsHttps)
            {
                SetHeader(context, "Strict-Transport-Security", $"max-age={Math.Max(1, _options.HstsMaxAgeDays) * 86400}; includeSubDomains");
            }

            return Task.CompletedTask;
        });

        await next(context);
    }

    private static void SetHeader(HttpContext context, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value) && !context.Response.Headers.ContainsKey(name))
        {
            context.Response.Headers[name] = value;
        }
    }
}
