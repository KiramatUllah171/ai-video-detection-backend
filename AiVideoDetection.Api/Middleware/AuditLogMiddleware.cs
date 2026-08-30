using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using AiVideoDetection.Application.Admin.DTOs;
using AiVideoDetection.Application.Admin.Interfaces;

namespace AiVideoDetection.Api.Middleware;

public sealed class AuditLogMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InvokeAsync(HttpContext context, IAuditLogService auditLogService)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? capturedException = null;

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            capturedException = exception;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            if (ShouldLog(context, capturedException))
            {
                var userId = TryGetUserId(context.User);
                await auditLogService.LogAsync(new AuditLogCreateDto
                {
                    UserId = userId,
                    UserName = context.User.FindFirstValue(ClaimTypes.Name),
                    UserEmail = context.User.FindFirstValue(ClaimTypes.Email),
                    Category = GetCategory(context),
                    Action = GetAction(context),
                    Severity = capturedException is not null || context.Response.StatusCode >= 500
                        ? "Error"
                        : context.Response.StatusCode >= 400
                            ? "Warning"
                            : "Information",
                    Message = GetMessage(context, capturedException),
                    ResourceType = GetResourceType(context),
                    ResourceId = GetResourceId(context),
                    HttpMethod = context.Request.Method,
                    Path = context.Request.Path.Value,
                    StatusCode = capturedException is null ? context.Response.StatusCode : StatusCodes.Status500InternalServerError,
                    IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = context.Request.Headers.UserAgent.ToString(),
                    DetailsJson = JsonSerializer.Serialize(new
                    {
                        durationMs = stopwatch.ElapsedMilliseconds,
                        query = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : null,
                        error = capturedException?.Message
                    }, JsonOptions)
                }, CancellationToken.None);
            }
        }
    }

    private static bool ShouldLog(HttpContext context, Exception? exception)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/admin/logs", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/refresh", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/videos/history", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase) && exception is null)
        {
            return false;
        }

        if (exception is not null || context.Response.StatusCode >= 400)
        {
            return true;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return true;
        }

        return path.Contains("/report/pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static long? TryGetUserId(ClaimsPrincipal user)
    {
        return long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            ? userId
            : null;
    }

    private static string GetCategory(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("/auth/", StringComparison.OrdinalIgnoreCase)) return "Authentication";
        if (path.Contains("/report/pdf", StringComparison.OrdinalIgnoreCase)) return "Report";
        if (path.Contains("/admin/", StringComparison.OrdinalIgnoreCase)) return "Admin";
        if (path.Contains("/videos", StringComparison.OrdinalIgnoreCase)) return "Video";
        if (path.Contains("/jobs", StringComparison.OrdinalIgnoreCase)) return "Processing";
        return "Api";
    }

    private static string GetAction(HttpContext context)
    {
        var method = context.Request.Method;
        var path = context.Request.Path.Value ?? string.Empty;

        if (path.EndsWith("/auth/signup", StringComparison.OrdinalIgnoreCase)) return "SignupRequested";
        if (path.EndsWith("/auth/login", StringComparison.OrdinalIgnoreCase)) return "LoginRequested";
        if (path.EndsWith("/auth/logout", StringComparison.OrdinalIgnoreCase)) return "LogoutRequested";
        if (path.EndsWith("/auth/forgot-password", StringComparison.OrdinalIgnoreCase)) return "PasswordResetRequested";
        if (path.EndsWith("/auth/reset-password", StringComparison.OrdinalIgnoreCase)) return "PasswordResetCompleted";
        if (path.EndsWith("/auth/confirm-email", StringComparison.OrdinalIgnoreCase)) return "EmailConfirmed";
        if (path.EndsWith("/auth/resend-confirmation-email", StringComparison.OrdinalIgnoreCase)) return "EmailConfirmationResent";
        if (path.EndsWith("/report/pdf", StringComparison.OrdinalIgnoreCase)) return "ReportDownload";
        if (path.Contains("/retry-analysis", StringComparison.OrdinalIgnoreCase)) return "RetryAnalysis";
        if (path.Contains("/reanalyze", StringComparison.OrdinalIgnoreCase)) return "ReanalyzeVideo";
        if (path.Contains("/cancel-analysis", StringComparison.OrdinalIgnoreCase)) return "CancelAnalysis";
        if (path.Contains("/pause-analysis", StringComparison.OrdinalIgnoreCase)) return "PauseAnalysis";
        if (path.Contains("/resume-analysis", StringComparison.OrdinalIgnoreCase)) return "ResumeAnalysis";
        if (path.Contains("/admin/jobs/", StringComparison.OrdinalIgnoreCase) && path.EndsWith("/retry", StringComparison.OrdinalIgnoreCase)) return "AdminRetryJob";
        if (path.Contains("/admin/users/", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPatch(method)) return "UpdateUserStatus";
        if (path.Contains("/videos", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsPost(method)) return "UploadVideo";
        if (path.Contains("/videos", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsDelete(method)) return "DeleteVideo";
        return $"{method} {path}";
    }

    private static string GetMessage(HttpContext context, Exception? exception)
    {
        if (exception is not null)
        {
            return "Unhandled API error occurred.";
        }

        var action = GetAction(context);
        return context.Response.StatusCode >= 400
            ? $"{action} failed with HTTP {context.Response.StatusCode}."
            : $"{action} completed.";
    }

    private static string? GetResourceType(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("/videos/", StringComparison.OrdinalIgnoreCase)) return "Video";
        if (path.Contains("/users/", StringComparison.OrdinalIgnoreCase)) return "User";
        if (path.Contains("/jobs/", StringComparison.OrdinalIgnoreCase)) return "Job";
        return null;
    }

    private static string? GetResourceId(HttpContext context)
    {
        var segments = context.Request.Path.Value?
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        for (var index = 0; index < segments.Length - 1; index++)
        {
            if ((segments[index].Equals("videos", StringComparison.OrdinalIgnoreCase) ||
                 segments[index].Equals("users", StringComparison.OrdinalIgnoreCase) ||
                 segments[index].Equals("jobs", StringComparison.OrdinalIgnoreCase)) &&
                long.TryParse(segments[index + 1], out _))
            {
                return segments[index + 1];
            }
        }

        return null;
    }
}
