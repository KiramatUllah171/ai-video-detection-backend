using System.Security.Claims;
using System.Net;
using System.Threading.RateLimiting;
using AiVideoDetection.Api.Authorization;
using AiVideoDetection.Api.Health;
using AiVideoDetection.Api.Middleware;
using AiVideoDetection.Api.Options;
using AiVideoDetection.Application;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Payments.Options;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure;
using AiVideoDetection.Infrastructure.Data;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var requestLimits = builder.Configuration
    .GetSection(RequestLimitOptions.SectionName)
    .Get<RequestLimitOptions>() ?? new RequestLimitOptions();
var dataProtectionOptions = builder.Configuration
    .GetSection(ApplicationDataProtectionOptions.SectionName)
    .Get<ApplicationDataProtectionOptions>() ?? new ApplicationDataProtectionOptions();
var databaseOptions = builder.Configuration
    .GetSection(DatabaseOptions.SectionName)
    .Get<DatabaseOptions>() ?? new DatabaseOptions();
var forwardedHeadersOptions = builder.Configuration
    .GetSection(ForwardedHeadersSecurityOptions.SectionName)
    .Get<ForwardedHeadersSecurityOptions>() ?? new ForwardedHeadersSecurityOptions();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = requestLimits.MaxUploadBodySizeBytes;
});

builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    loggerConfiguration
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "SachAI.Api")
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console();
});

ValidateProductionConfiguration(builder.Configuration, builder.Environment);

builder.Services.AddControllers();
builder.Services.AddMemoryCache();
builder.Services.AddOptions<ApplicationDataProtectionOptions>()
    .Bind(builder.Configuration.GetSection(ApplicationDataProtectionOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApplicationName), "DataProtection:ApplicationName is required.")
    .ValidateOnStart();
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<DeploymentReadinessOptions>()
    .Bind(builder.Configuration.GetSection(DeploymentReadinessOptions.SectionName))
    .ValidateOnStart();

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName(dataProtectionOptions.ApplicationName);
if (!string.IsNullOrWhiteSpace(dataProtectionOptions.KeyRingPath))
{
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionOptions.KeyRingPath));
}

builder.Services.AddOptions<RequestLimitOptions>()
    .Bind(builder.Configuration.GetSection(RequestLimitOptions.SectionName))
    .Validate(options => options.MaxApiBodySizeBytes is > 0 and <= 10_485_760, "RequestLimits:MaxApiBodySizeBytes must be between 1 byte and 10 MB.")
    .Validate(options => options.MaxUploadBodySizeBytes == VideoUploadSizeLimits.MultipartRequestBodyLimitBytes, $"RequestLimits:MaxUploadBodySizeBytes must be {VideoUploadSizeLimits.MultipartRequestBodyLimitBytes}.")
    .ValidateOnStart();
builder.Services.AddOptions<SecurityHeadersOptions>()
    .Bind(builder.Configuration.GetSection(SecurityHeadersOptions.SectionName))
    .Validate(options => options.HstsMaxAgeDays > 0, "SecurityHeaders:HstsMaxAgeDays must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<RateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
    .Validate(options => options.WindowSeconds > 0, "RateLimiting:WindowSeconds must be greater than zero.")
    .Validate(options => options.LoginPermitLimit > 0, "RateLimiting:LoginPermitLimit must be greater than zero.")
    .Validate(options => options.AuthSensitivePermitLimit > 0, "RateLimiting:AuthSensitivePermitLimit must be greater than zero.")
    .Validate(options => options.RefreshPermitLimit > 0, "RateLimiting:RefreshPermitLimit must be greater than zero.")
    .Validate(options => options.UploadPermitLimit > 0, "RateLimiting:UploadPermitLimit must be greater than zero.")
    .Validate(options => options.VideoActionPermitLimit > 0, "RateLimiting:VideoActionPermitLimit must be greater than zero.")
    .Validate(options => options.SubscriptionPermitLimit > 0, "RateLimiting:SubscriptionPermitLimit must be greater than zero.")
    .Validate(options => options.PaymentPermitLimit > 0, "RateLimiting:PaymentPermitLimit must be greater than zero.")
    .Validate(options => options.PaymentCallbackPermitLimit > 0, "RateLimiting:PaymentCallbackPermitLimit must be greater than zero.")
    .Validate(options => options.ReportDownloadPermitLimit > 0, "RateLimiting:ReportDownloadPermitLimit must be greater than zero.")
    .Validate(options => options.AdminPermitLimit > 0, "RateLimiting:AdminPermitLimit must be greater than zero.")
    .Validate(options => options.GeneralApiPermitLimit > 0, "RateLimiting:GeneralApiPermitLimit must be greater than zero.")
    .ValidateOnStart();
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = requestLimits.MaxUploadBodySizeBytes;
    options.ValueLengthLimit = 1024 * 1024;
    options.MultipartHeadersLengthLimit = 128 * 1024;
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.ForwardLimit = Math.Max(1, forwardedHeadersOptions.ForwardLimit);

    if (forwardedHeadersOptions.KnownProxies.Length > 0 || forwardedHeadersOptions.KnownNetworks.Length > 0)
    {
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
    }

    foreach (var proxy in forwardedHeadersOptions.KnownProxies)
    {
        if (IPAddress.TryParse(proxy, out var address))
        {
            options.KnownProxies.Add(address);
        }
    }

    foreach (var network in forwardedHeadersOptions.KnownNetworks)
    {
        var parsedNetwork = ParseIpNetwork(network);
        if (parsedNetwork is not null)
        {
            options.KnownIPNetworks.Add(parsedNetwork.Value);
        }
    }
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SachAI API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter JWT token. If Swagger uses HTTP bearer scheme, paste only the token without the word Bearer."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
});

builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services
    .AddHealthChecks()
    .AddCheck("api", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<StorageHealthCheck>("storage", tags: ["ready"])
    .AddCheck<HangfireHealthCheck>("background-worker", tags: ["ready"])
    .AddCheck<AiServiceHealthCheck>("ai-service", tags: ["ready"])
    .AddCheck<ProviderConfigurationHealthCheck>("provider-configuration", tags: ["ready"]);

var hangfireConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

builder.Services.AddHangfire(configuration =>
{
    configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(
            storage => storage.UseNpgsqlConnection(hangfireConnectionString, null),
            new PostgreSqlStorageOptions
            {
                PrepareSchemaIfNecessary = true,
                QueuePollInterval = TimeSpan.FromSeconds(5),
                InvisibilityTimeout = TimeSpan.FromMinutes(30)
            });
});

builder.Services.AddHangfireServer(options =>
{
    options.Queues = ["analysis"];
    options.WorkerCount = Math.Max(1, Environment.ProcessorCount / 2);
});

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? ["http://localhost:3000", "http://localhost:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("ReactFrontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy
                .SetIsOriginAllowed(origin => IsAllowedDevelopmentFrontendOrigin(origin, allowedOrigins))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();

            return;
        }

        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ActiveUserOnly", policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ActiveConfirmedUserRequirement(requireConfirmedEmail: false)));
    options.AddPolicy("AdminOnly", policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("Admin")
        .AddRequirements(new ActiveConfirmedUserRequirement(requireConfirmedEmail: true)));
    options.AddPolicy("EmailConfirmed", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("email_confirmed", "true")
        .AddRequirements(new ActiveConfirmedUserRequirement(requireConfirmedEmail: true)));
    options.AddPolicy("OwnVideoOnly", policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ActiveConfirmedUserRequirement(requireConfirmedEmail: true))
        .AddRequirements(new VideoOwnerRequirement("Video")));
    options.AddPolicy("OwnReportOnly", policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new ActiveConfirmedUserRequirement(requireConfirmedEmail: true))
        .AddRequirements(new VideoOwnerRequirement("Report")));
});
builder.Services.AddScoped<IAuthorizationHandler, ActiveConfirmedUserAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, VideoOwnerAuthorizationHandler>();

var rateLimitingOptions = builder.Configuration
    .GetSection(RateLimitingOptions.SectionName)
    .Get<RateLimitingOptions>() ?? new RateLimitingOptions();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString("0");
        }

        context.HttpContext.Response.ContentType = "application/json";
        var correlationId = context.HttpContext.RequestServices
            .GetService<ICorrelationIdAccessor>()?
            .CorrelationId ?? context.HttpContext.TraceIdentifier;
        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiResponse<object>.ErrorResponse(
                "Too many requests. Please wait a moment and try again.",
                correlationId: correlationId),
            cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        if (!rateLimitingOptions.Enabled ||
            HttpMethods.IsOptions(context.Request.Method) ||
            !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitPartition.GetNoLimiter("unlimited");
        }

        var profile = GetRateLimitProfile(context, rateLimitingOptions);
        var partitionKey = $"{profile.Name}:{GetRateLimitClientKey(context)}";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = profile.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitingOptions.WindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    if (app.Environment.IsDevelopment() || databaseOptions.ApplyMigrationsOnStartup)
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }
    else
    {
        var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        startupLogger.LogInformation("Database migrations are not applied on startup. Apply migrations through the release process before serving production traffic.");
    }

    var toolValidator = scope.ServiceProvider.GetRequiredService<IVideoProcessingToolValidator>();
    _ = await toolValidator.ValidateAsync();
}

if (forwardedHeadersOptions.Enabled)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("ReactFrontend");
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault() ?? httpContext.TraceIdentifier);
        diagnosticContext.Set("UserId", httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));
        diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString());
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestPath", httpContext.Request.Path.Value);
        diagnosticContext.Set("StatusCode", httpContext.Response.StatusCode);
    };
});
app.UseMiddleware<ApiResponseLocalizationMiddleware>();
app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
app.UseMiddleware<RequestBodySizeLimitMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseHangfireDashboard("/hangfire");
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.UseMiddleware<AuditLogMiddleware>();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = healthCheck => healthCheck.Tags.Contains("live"),
    ResponseWriter = WriteSafeHealthResponseAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = healthCheck => healthCheck.Tags.Contains("ready"),
    ResponseWriter = WriteSafeHealthResponseAsync,
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    }
});
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
    recurringJobManager.AddOrUpdate<IRetentionCleanupService>(
        "cleanup-temporary-video-files",
        "analysis",
        service => service.CleanupTemporaryFilesAsync(),
        Cron.Hourly());

    recurringJobManager.AddOrUpdate<IRetentionCleanupService>(
        "cleanup-expired-video-retention-assets",
        "analysis",
        service => service.CleanupExpiredRetainedAssetsAsync(),
        Cron.Hourly());

    recurringJobManager.AddOrUpdate<IScanReservationReconciliationService>(
        "reconcile-scan-reservations",
        "analysis",
        service => service.ReconcileAsync(),
        Cron.Hourly());
}

app.Run();

static void ValidateProductionConfiguration(IConfiguration configuration, IWebHostEnvironment environment)
{
    if (environment.IsDevelopment())
    {
        return;
    }

    var connectionString = configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrWhiteSpace(connectionString)
        || connectionString.Contains("Password=123456789", StringComparison.OrdinalIgnoreCase)
        || connectionString.Contains("Host=localhost", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Production database connection string must be provided through secure configuration.");
    }

    var jwtSecret = configuration["Jwt:Secret"];
    if (string.IsNullOrWhiteSpace(jwtSecret)
        || jwtSecret.Length < 32
        || string.Equals(jwtSecret, "development-only-jwt-secret-change-before-production", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Production JWT secret must be provided through secure configuration.");
    }

    if (string.IsNullOrWhiteSpace(configuration["AiService:ApiKey"]))
    {
        throw new InvalidOperationException("Production AI service API key must be configured.");
    }

    var googleClientId = FirstConfigured(
        configuration["GoogleAuth:ClientId"],
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID"),
        Environment.GetEnvironmentVariable("GoogleAuth__ClientId"));
    var googleClientSecret = FirstConfigured(
        configuration["GoogleAuth:ClientSecret"],
        Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET"),
        Environment.GetEnvironmentVariable("GoogleAuth__ClientSecret"));
    if (string.IsNullOrWhiteSpace(googleClientId) || string.IsNullOrWhiteSpace(googleClientSecret))
    {
        throw new InvalidOperationException("Production Google OAuth credentials must be provided through secure configuration.");
    }

    if (string.Equals(configuration["PasswordReset:Provider"], "Smtp", StringComparison.OrdinalIgnoreCase))
    {
        var smtpHost = FirstConfigured(configuration["PasswordReset:Host"], configuration["PasswordReset:SmtpHost"]);
        var smtpUsername = FirstConfigured(configuration["PasswordReset:Username"], configuration["PasswordReset:SmtpUsername"]);
        var smtpPassword = FirstConfigured(
            configuration["PasswordReset:Password"],
            configuration["PasswordReset:SmtpPassword"],
            Environment.GetEnvironmentVariable("GMAIL_APP_PASSWORD"),
            Environment.GetEnvironmentVariable("SMTP_PASSWORD"));
        var senderEmail = FirstConfigured(configuration["PasswordReset:SenderEmail"], configuration["PasswordReset:FromEmail"]);

        if (string.IsNullOrWhiteSpace(smtpHost))
        {
            throw new InvalidOperationException("Production SMTP host must be configured.");
        }

        if (string.IsNullOrWhiteSpace(smtpUsername))
        {
            throw new InvalidOperationException("Production SMTP username must be provided through secure configuration.");
        }

        if (string.IsNullOrWhiteSpace(smtpPassword))
        {
            throw new InvalidOperationException("Production SMTP password must be provided through secure configuration.");
        }

        if (string.IsNullOrWhiteSpace(senderEmail))
        {
            throw new InvalidOperationException("Production SMTP sender email must be configured.");
        }

    }

    if (string.IsNullOrWhiteSpace(configuration["DataProtection:KeyRingPath"]))
    {
        throw new InvalidOperationException("Production Data Protection key ring path must be provided through secure configuration.");
    }

    var encryptionOptions = configuration.GetSection(ApplicationEncryptionOptions.SectionName).Get<ApplicationEncryptionOptions>() ?? new ApplicationEncryptionOptions();
    encryptionOptions.MasterKeyBase64 =
        Environment.GetEnvironmentVariable("ENCRYPTION_MASTER_KEY_BASE64")
        ?? Environment.GetEnvironmentVariable("Encryption__MasterKeyBase64")
        ?? encryptionOptions.MasterKeyBase64;
    if (!encryptionOptions.Enabled
        || !encryptionOptions.EncryptStorageObjects
        || !encryptionOptions.EncryptDatabaseFields
        || !IsValidEncryptionKey(encryptionOptions.MasterKeyBase64))
    {
        throw new InvalidOperationException("Production encryption must be enabled with a secure 32-byte master key.");
    }

    var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    if (allowedOrigins.Length == 0
        || allowedOrigins.Any(origin => !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || origin.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            || origin.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || origin == "*"))
    {
        throw new InvalidOperationException("Production CORS allowed origins must contain only HTTPS frontend domains.");
    }

    var allowedHosts = configuration["AllowedHosts"];
    if (string.IsNullOrWhiteSpace(allowedHosts) || allowedHosts == "*")
    {
        throw new InvalidOperationException("Production allowed hosts must be explicitly configured.");
    }

    var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
    if (databaseOptions.ApplyMigrationsOnStartup)
    {
        throw new InvalidOperationException("Production database migrations must be applied through the release process, not on application startup.");
    }

    var paymentOptions = configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>() ?? new PaymentOptions();
    if (paymentOptions.MockEnabled)
    {
        throw new InvalidOperationException("Mock payments must be disabled in Production.");
    }

    var subscriptionHmacSecret = Environment.GetEnvironmentVariable("SUBSCRIPTION_HMAC_SECRET")
        ?? Environment.GetEnvironmentVariable("SubscriptionSecurity__HmacSecret")
        ?? configuration["SubscriptionSecurity:HmacSecret"];
    if (string.IsNullOrWhiteSpace(subscriptionHmacSecret) || subscriptionHmacSecret.Length < 32)
    {
        throw new InvalidOperationException("Production subscription HMAC secret must be provided through secure configuration.");
    }

    var forwardedHeaders = configuration.GetSection(ForwardedHeadersSecurityOptions.SectionName).Get<ForwardedHeadersSecurityOptions>() ?? new ForwardedHeadersSecurityOptions();
    if (forwardedHeaders.Enabled &&
        !forwardedHeaders.KnownProxies.Any(proxy => IPAddress.TryParse(proxy, out _)) &&
        !forwardedHeaders.KnownNetworks.Any(network => ParseIpNetwork(network) is not null))
    {
        throw new InvalidOperationException("Production forwarded headers require valid trusted proxies or networks.");
    }

    var securityHeaders = configuration.GetSection(SecurityHeadersOptions.SectionName).Get<SecurityHeadersOptions>() ?? new SecurityHeadersOptions();
    if (!securityHeaders.EnableHsts)
    {
        throw new InvalidOperationException("Production HSTS must be enabled.");
    }

    var readiness = configuration.GetSection(DeploymentReadinessOptions.SectionName).Get<DeploymentReadinessOptions>() ?? new DeploymentReadinessOptions();
    if (!readiness.RequireHttps)
    {
        throw new InvalidOperationException("Production HTTPS enforcement must remain enabled.");
    }

    if (!readiness.DatabaseBackupConfigured)
    {
        throw new InvalidOperationException("Production database backup must be configured before deployment.");
    }

    if (!readiness.ReportStorageBackupConfigured)
    {
        throw new InvalidOperationException("Production report storage backup policy must be configured before deployment.");
    }

    if (!readiness.MigrationReleaseProcessConfigured)
    {
        throw new InvalidOperationException("Production migration release process must be configured before deployment.");
    }

    if (!readiness.HealthChecksConnected)
    {
        throw new InvalidOperationException("Production health checks must be connected to the hosting platform before deployment.");
    }

    if (!readiness.MonitoringDashboardConfigured || string.IsNullOrWhiteSpace(readiness.MonitoringDashboardUrl))
    {
        throw new InvalidOperationException("Production monitoring dashboard must be configured before deployment.");
    }
}

static string? FirstConfigured(params string?[] values)
{
    return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}

static bool IsValidEncryptionKey(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    try
    {
        return Convert.FromBase64String(value).Length == 32;
    }
    catch (FormatException)
    {
        return false;
    }
}

static RateLimitProfile GetRateLimitProfile(HttpContext context, RateLimitingOptions options)
{
    var path = context.Request.Path.Value ?? string.Empty;

    if (path.StartsWith("/api/auth/login", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("auth-login", options.LoginPermitLimit);
    }

    if (path.StartsWith("/api/auth/refresh", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("auth-refresh", options.RefreshPermitLimit);
    }

    if (path.StartsWith("/api/auth/signup", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/google", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/forgot-password", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/reset-password", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/confirm-email", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/check-password-reset", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/check-email-confirmation", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/decline-email-confirmation", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/auth/resend-confirmation-email", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("auth-sensitive", options.AuthSensitivePermitLimit);
    }

    if (path.StartsWith("/api/videos/upload", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/api/videos/guest-upload", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("video-upload", options.UploadPermitLimit);
    }

    if (path.Contains("/retry-analysis", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/reanalyze", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/cancel-analysis", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/pause-analysis", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("/resume-analysis", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("video-action", options.VideoActionPermitLimit);
    }

    if (path.StartsWith("/api/subscriptions", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("subscriptions", options.SubscriptionPermitLimit);
    }

    if (path.StartsWith("/api/payments/easypaisa/callback", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("payment-callback", options.PaymentCallbackPermitLimit);
    }

    if (path.StartsWith("/api/payments", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("payments", options.PaymentPermitLimit);
    }

    if (path.EndsWith("/report/pdf", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("report-download", options.ReportDownloadPermitLimit);
    }

    if (path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase))
    {
        return new RateLimitProfile("admin-api", options.AdminPermitLimit);
    }

    return new RateLimitProfile("general-api", options.GeneralApiPermitLimit);
}

static string GetRateLimitClientKey(HttpContext context)
{
    var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!string.IsNullOrWhiteSpace(userId))
    {
        return $"user:{userId}";
    }

    return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
}

static Task WriteSafeHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";

    var response = new
    {
        status = report.Status.ToString(),
        durationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 2),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 2)
        })
    };

    return context.Response.WriteAsJsonAsync(response);
}

static bool IsAllowedDevelopmentFrontendOrigin(string origin, string[] configuredOrigins)
{
    if (configuredOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (uri.Port is not (3000 or 5173))
    {
        return false;
    }

    if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
    {
        return true;
    }

    if (!IPAddress.TryParse(uri.Host, out var address))
    {
        return false;
    }

    return IPAddress.IsLoopback(address) || IsPrivateIpv4Address(address);
}

static bool IsPrivateIpv4Address(IPAddress address)
{
    var bytes = address.GetAddressBytes();
    if (bytes.Length != 4)
    {
        return false;
    }

    return bytes[0] == 10
        || bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31
        || bytes[0] == 192 && bytes[1] == 168;
}

static System.Net.IPNetwork? ParseIpNetwork(string value)
{
    if (!System.Net.IPNetwork.TryParse(value, out var network))
    {
        return null;
    }

    return network;
}

internal sealed record RateLimitProfile(string Name, int PermitLimit);
