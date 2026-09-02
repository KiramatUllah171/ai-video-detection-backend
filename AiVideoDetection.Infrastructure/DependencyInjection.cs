using AiVideoDetection.Application.Admin.Interfaces;
using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Admin;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Auth;
using AiVideoDetection.Infrastructure.Common;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Storage;
using AiVideoDetection.Infrastructure.Videos;
using AiVideoDetection.Infrastructure.Videos.Ai;
using AiVideoDetection.Infrastructure.Videos.Matching;
using AiVideoDetection.Infrastructure.Videos.Processing;
using AiVideoDetection.Infrastructure.Videos.Retention;
using AiVideoDetection.Infrastructure.Videos.Reports;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Npgsql.NameTranslation;
using System.Text;

namespace AiVideoDetection.Infrastructure;

public static class DependencyInjection
{
    private static readonly NpgsqlNullNameTranslator EnumNameTranslator = new();

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                npgsqlOptions =>
                {
                    npgsqlOptions.MapEnum<UserRole>(
                        "user_role",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MapEnum<VideoStatus>(
                        "video_status",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MapEnum<JobStatus>(
                        "job_status",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MapEnum<AnalysisSegmentStatus>(
                        "analysis_segment_status",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MapEnum<AnalysisLabel>(
                        "analysis_label",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MapEnum<EvidenceType>(
                        "evidence_type",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MapEnum<EvidenceSeverity>(
                        "evidence_severity",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MapEnum<ConfidenceLevel>(
                        "confidence_level",
                        nameTranslator: EnumNameTranslator);
                    npgsqlOptions.MigrationsAssembly("AiVideoDetection.Api");
                }));

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<PasswordResetOptions>(configuration.GetSection(PasswordResetOptions.SectionName));
        services.AddOptions<AuthSecurityOptions>()
            .Bind(configuration.GetSection(AuthSecurityOptions.SectionName))
            .Validate(options => options.MaxFailedAccessAttempts is >= 1 and <= 20, "AuthSecurity:MaxFailedAccessAttempts must be 1-20.")
            .Validate(options => options.LockoutMinutes is >= 1 and <= 1440, "AuthSecurity:LockoutMinutes must be 1-1440.")
            .Validate(options => options.EmailThrottleWindowMinutes is >= 1 and <= 1440, "AuthSecurity:EmailThrottleWindowMinutes must be 1-1440.")
            .Validate(options => options.LoginEmailPermitLimit is >= 1 and <= 1000, "AuthSecurity:LoginEmailPermitLimit must be 1-1000.")
            .Validate(options => options.PasswordResetEmailPermitLimit is >= 1 and <= 100, "AuthSecurity:PasswordResetEmailPermitLimit must be 1-100.")
            .Validate(options => options.EmailConfirmationResendPermitLimit is >= 1 and <= 100, "AuthSecurity:EmailConfirmationResendPermitLimit must be 1-100.")
            .Validate(options => options.ReportDownloadPerReportPermitLimit is >= 1 and <= 1000, "AuthSecurity:ReportDownloadPerReportPermitLimit must be 1-1000.")
            .ValidateOnStart();
        services.PostConfigure<PasswordResetOptions>(options =>
        {
            var smtpPassword = Environment.GetEnvironmentVariable("PasswordReset__Password")
                ?? Environment.GetEnvironmentVariable("GMAIL_APP_PASSWORD")
                ?? Environment.GetEnvironmentVariable("SMTP_PASSWORD");
            if (!string.IsNullOrWhiteSpace(smtpPassword))
            {
                options.Password = smtpPassword;
                options.SmtpPassword = smtpPassword;
            }
        });
        services.AddOptions<VideoUploadOptions>()
            .Bind(configuration.GetSection(VideoUploadOptions.SectionName))
            .Validate(options => options.MaxFileSizeBytes == 524_288_000, "VideoUpload:MaxFileSizeBytes must be 524288000.")
            .Validate(options => options.SmartScanMaxFileSizeBytes == 209_715_200, "VideoUpload:SmartScanMaxFileSizeBytes must be 209715200.")
            .Validate(options => options.DetailedScanMaxFileSizeBytes == 524_288_000, "VideoUpload:DetailedScanMaxFileSizeBytes must be 524288000.")
            .Validate(options => options.UploadChunkSizeBytes is >= 5_242_880 and <= 20_971_520, "VideoUpload:UploadChunkSizeBytes must be 5-20 MB.")
            .Validate(options => options.AllowedExtensions.Length > 0 && options.AllowedContentTypes.Length > 0, "VideoUpload allowed types must be configured.")
            .ValidateOnStart();
        services.AddOptions<VideoProcessingOptions>()
            .Bind(configuration.GetSection(VideoProcessingOptions.SectionName))
            .Validate(options => options.SmartScanClipDurationSeconds > 0, "Smart scan clip duration must be positive.")
            .Validate(options => options.MaxSegmentCount is >= 1 and <= 100, "Max segment count must be 1-100.")
            .Validate(options => options.SegmentConcurrency is >= 1 and <= 8, "Segment concurrency must be 1-8.")
            .Validate(options => options.MaxConcurrentProviderRequests is >= 1 and <= 32, "Max concurrent provider requests must be 1-32.")
            .Validate(options => options.FfmpegTimeoutSeconds is >= 10 and <= 3600, "FFmpeg timeout must be 10-3600 seconds.")
            .Validate(options => options.FfprobeTimeoutSeconds is >= 10 and <= 600, "FFprobe timeout must be 10-600 seconds.")
            .Validate(options => options.WorkerTimeoutMinutes is >= 1 and <= 240, "Worker timeout must be 1-240 minutes.")
            .Validate(options => options.MinimumRequiredCoverageRatio is > 0 and <= 1, "Minimum coverage ratio must be between 0 and 1.")
            .Validate(options => options.TemporaryFileRetentionHours >= 1, "Temporary file retention must be at least 1 hour.")
            .Validate(options => options.OriginalVideoRetentionDays >= 1, "Original video retention must be at least 1 day.")
            .Validate(options => options.ReportRetention >= options.OriginalVideoRetention, "Report retention must be greater than or equal to original video retention.")
            .Validate(options => options.DetailedResultRetention >= options.ReportRetention, "Detailed result retention must be greater than or equal to report retention.")
            .ValidateOnStart();
        services.AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection(MonitoringOptions.SectionName))
            .Validate(options => options.AlertSuppressionMinutes is >= 1 and <= 1440, "Monitoring:AlertSuppressionMinutes must be 1-1440.")
            .Validate(options => options.FailedLoginThreshold is >= 1 and <= 1000, "Monitoring:FailedLoginThreshold must be 1-1000.")
            .Validate(options => options.FailedLoginWindowMinutes is >= 1 and <= 1440, "Monitoring:FailedLoginWindowMinutes must be 1-1440.")
            .Validate(options => options.ApiErrorThreshold is >= 1 and <= 1000, "Monitoring:ApiErrorThreshold must be 1-1000.")
            .Validate(options => options.ApiErrorWindowMinutes is >= 1 and <= 1440, "Monitoring:ApiErrorWindowMinutes must be 1-1440.")
            .Validate(options => options.ProviderFailureThreshold is >= 1 and <= 1000, "Monitoring:ProviderFailureThreshold must be 1-1000.")
            .Validate(options => options.ProviderFailureWindowMinutes is >= 1 and <= 1440, "Monitoring:ProviderFailureWindowMinutes must be 1-1440.")
            .Validate(options => options.CleanupFailureThreshold is >= 1 and <= 1000, "Monitoring:CleanupFailureThreshold must be 1-1000.")
            .Validate(options => options.CleanupFailureWindowMinutes is >= 1 and <= 1440, "Monitoring:CleanupFailureWindowMinutes must be 1-1440.")
            .Validate(options => options.ProductionLogRetentionDays is >= 7 and <= 2555, "Monitoring:ProductionLogRetentionDays must be 7-2555.")
            .ValidateOnStart();
        services.AddOptions<ApplicationEncryptionOptions>()
            .Bind(configuration.GetSection(ApplicationEncryptionOptions.SectionName))
            .Validate(options =>
                !options.Enabled
                || (!options.EncryptStorageObjects && !options.EncryptDatabaseFields)
                || IsValidEncryptionKey(options.MasterKeyBase64),
                "Encryption:MasterKeyBase64 must be a valid base64-encoded 32-byte key when encryption is enabled.")
            .ValidateOnStart();
        services.AddOptions<AiServiceOptions>()
            .Bind(configuration.GetSection(AiServiceOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "AiService:BaseUrl is required.")
            .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "AiService:BaseUrl must be an absolute URI.")
            .Validate(options => options.TimeoutSeconds is >= 1 and <= 600, "AiService:TimeoutSeconds must be 1-600 seconds.")
            .Validate(options => options.TransientRetryCount is >= 0 and <= 5, "AiService:TransientRetryCount must be 0-5.")
            .Validate(options => options.TransientRetryBackoffSeconds is >= 1 and <= 60, "AiService:TransientRetryBackoffSeconds must be 1-60 seconds.")
            .Validate(options => options.CircuitBreakerFailureThreshold is >= 1 and <= 100, "AiService:CircuitBreakerFailureThreshold must be 1-100.")
            .Validate(options => options.CircuitBreakerBreakSeconds is >= 10 and <= 3600, "AiService:CircuitBreakerBreakSeconds must be 10-3600 seconds.")
            .Validate(options => options.MaxFramesPerRequest is >= 1 and <= 120, "AiService:MaxFramesPerRequest must be 1-120.")
            .ValidateOnStart();
        services.PostConfigure<AiServiceOptions>(options =>
        {
            options.ProviderMode = Environment.GetEnvironmentVariable("AI_PROVIDER")
                ?? Environment.GetEnvironmentVariable("PROVIDER_MODE")
                ?? options.ProviderMode;
            options.BitMindEnabled = bool.TryParse(Environment.GetEnvironmentVariable("BITMIND_ENABLED"), out var bitMindEnabled)
                ? bitMindEnabled
                : options.BitMindEnabled;
            options.BitMindMonthlyQuota = int.TryParse(Environment.GetEnvironmentVariable("BITMIND_MONTHLY_QUOTA"), out var quota)
                ? quota
                : options.BitMindMonthlyQuota;
            options.LocalFallbackEnabled = bool.TryParse(Environment.GetEnvironmentVariable("LOCAL_FALLBACK_ENABLED"), out var fallbackEnabled)
                ? fallbackEnabled
                : options.LocalFallbackEnabled;
            options.ExternalProviderPolicy = Environment.GetEnvironmentVariable("EXTERNAL_PROVIDER_POLICY") ?? options.ExternalProviderPolicy;
            options.ApiKey = Environment.GetEnvironmentVariable("AI_SERVICE_API_KEY") ?? options.ApiKey;
        });
        services.Configure<ScoringOptions>(configuration.GetSection(ScoringOptions.SectionName));
        services.Configure<InternalMatchingOptions>(configuration.GetSection(InternalMatchingOptions.SectionName));
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.PostConfigure<ApplicationEncryptionOptions>(options =>
        {
            var key = Environment.GetEnvironmentVariable("ENCRYPTION_MASTER_KEY_BASE64")
                ?? Environment.GetEnvironmentVariable("Encryption__MasterKeyBase64");
            if (!string.IsNullOrWhiteSpace(key))
            {
                options.MasterKeyBase64 = key;
            }
        });
        services
            .AddIdentityCore<User>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = Math.Clamp(
                    configuration.GetValue("AuthSecurity:MaxFailedAccessAttempts", 5),
                    1,
                    20);
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(Math.Clamp(
                    configuration.GetValue("AuthSecurity:LockoutMinutes", 15),
                    1,
                    1440));
                options.Password.RequiredLength = 8;
                options.Password.RequireUppercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<long>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
        services.AddHttpClient("AiService", (serviceProvider, client) =>
            {
                var aiOptions = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiServiceOptions>>().Value;
                client.BaseAddress = new Uri(aiOptions.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = TimeSpan.FromSeconds(Math.Clamp(aiOptions.TimeoutSeconds, 1, 600));
                if (!string.IsNullOrWhiteSpace(aiOptions.ApiKey))
                {
                    client.DefaultRequestHeaders.Add("X-AI-Service-Key", aiOptions.ApiKey);
                }
            })
            .SetHandlerLifetime(TimeSpan.FromMinutes(10));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAdminDashboardService, AdminDashboardService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        services.AddScoped<ICorrelationIdAccessor, CorrelationIdAccessor>();
        services.AddSingleton<IMonitoringAlertService, LoggingMonitoringAlertService>();
        services.AddSingleton<IApplicationEncryptionService, AesApplicationEncryptionService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IAuthThrottleService, InMemoryAuthThrottleService>();
        services.AddScoped<IPasswordResetEmailSender, SmtpAuthEmailSender>();
        services.AddScoped<IEmailConfirmationSender, SmtpAuthEmailSender>();
        services.AddScoped<IObjectStorageService, LocalObjectStorageService>();
        services.AddScoped<IVideoService, VideoService>();
        services.AddScoped<IAnalysisReportService, AnalysisReportService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IAnalysisJobQueue, HangfireAnalysisJobQueue>();
        services.AddScoped<IVideoProcessingService, VideoProcessingService>();
        services.AddScoped<IRetentionCleanupService, RetentionCleanupService>();
        services.AddScoped<IFrameExtractionService, FrameExtractionService>();
        services.AddScoped<IMetadataExtractionService, MetadataExtractionService>();
        services.AddScoped<IJobLogService, JobLogService>();
        services.AddScoped<IProcessRunner, ProcessRunner>();
        services.AddScoped<IFfmpegToolLocator, FfmpegToolLocator>();
        services.AddScoped<IVideoProcessingToolValidator, VideoProcessingToolValidator>();
        services.AddScoped<IAiInferenceClient, PythonAiInferenceClient>();
        services.AddSingleton<IProviderCircuitBreaker, InMemoryProviderCircuitBreaker>();
        services.AddSingleton<IProviderRequestGate, ProviderRequestGate>();
        services.AddScoped<IFinalScoringService, FinalScoringService>();
        services.AddScoped<IEvidenceGenerationService, EvidenceGenerationService>();
        services.AddScoped<IPerceptualHashProvider, PlaceholderPerceptualHashProvider>();
        services.AddScoped<IHammingDistanceService, HammingDistanceService>();
        services.AddScoped<IFrameHashService, FrameHashService>();
        services.AddScoped<IInternalVideoMatchingService, InternalVideoMatchingService>();
        services.AddScoped<AnalysisJobProcessor>();

        AddJwtAuthentication(services, configuration);

        return services;
    }

    private static void AddJwtAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("JWT configuration section is missing.");

        if (string.IsNullOrWhiteSpace(jwtOptions.Secret) || jwtOptions.Secret.Length < 32)
        {
            throw new InvalidOperationException("JWT secret must be configured and at least 32 characters long.");
        }

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret));

        services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.Zero
                };
            });
    }

    private static bool IsValidEncryptionKey(string? value)
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
}
