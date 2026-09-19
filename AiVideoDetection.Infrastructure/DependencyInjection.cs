using AiVideoDetection.Application.Admin.Interfaces;
using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Application.Common;
using AiVideoDetection.Application.Payments.Interfaces;
using AiVideoDetection.Application.Payments.Options;
using AiVideoDetection.Application.Subscriptions.Interfaces;
using AiVideoDetection.Application.Subscriptions.Options;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Infrastructure.Admin;
using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Auth;
using AiVideoDetection.Infrastructure.Common;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Payments;
using AiVideoDetection.Infrastructure.Storage;
using AiVideoDetection.Infrastructure.Subscriptions;
using AiVideoDetection.Infrastructure.Videos;
using AiVideoDetection.Infrastructure.Videos.Ai;
using AiVideoDetection.Infrastructure.Videos.Matching;
using AiVideoDetection.Infrastructure.Videos.Processing;
using AiVideoDetection.Infrastructure.Videos.Retention;
using AiVideoDetection.Infrastructure.Videos.Reports;
using AiVideoDetection.Infrastructure.Videos.StorageProtection;
using Amazon.Runtime;
using Amazon.S3;
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
        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.SectionName))
            .Validate(options => options.PendingPaymentExpiryMinutes is >= 5 and <= 1440, "Payments:PendingPaymentExpiryMinutes must be 5-1440.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.OrderPrefix), "Payments:OrderPrefix is required.")
            .ValidateOnStart();
        services.AddOptions<EasypaisaOptions>()
            .Bind(configuration.GetSection(EasypaisaOptions.SectionName))
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.CheckoutBaseUrl), "Easypaisa:CheckoutBaseUrl is required when Easypaisa is enabled.")
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.MerchantId), "Easypaisa:MerchantId is required when Easypaisa is enabled.")
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.StoreId), "Easypaisa:StoreId is required when Easypaisa is enabled.")
            .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.CallbackSecret), "Easypaisa:CallbackSecret is required when Easypaisa is enabled.")
            .Validate(options => string.IsNullOrWhiteSpace(options.CallbackSecret) || options.CallbackSecret.Length >= 32, "Easypaisa:CallbackSecret must be at least 32 characters.")
            .ValidateOnStart();
        services.PostConfigure<EasypaisaOptions>(options =>
        {
            options.MerchantId = Environment.GetEnvironmentVariable("EASYPAISA_MERCHANT_ID")
                ?? Environment.GetEnvironmentVariable("Easypaisa__MerchantId")
                ?? options.MerchantId;
            options.StoreId = Environment.GetEnvironmentVariable("EASYPAISA_STORE_ID")
                ?? Environment.GetEnvironmentVariable("Easypaisa__StoreId")
                ?? options.StoreId;
            options.CallbackSecret = Environment.GetEnvironmentVariable("EASYPAISA_CALLBACK_SECRET")
                ?? Environment.GetEnvironmentVariable("Easypaisa__CallbackSecret")
                ?? options.CallbackSecret;
        });
        services.AddOptions<SubscriptionSecurityOptions>()
            .Bind(configuration.GetSection(SubscriptionSecurityOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.DeviceCookieName), "SubscriptionSecurity:DeviceCookieName is required.")
            .Validate(options => options.DeviceCookieDays is >= 1 and <= 730, "SubscriptionSecurity:DeviceCookieDays must be 1-730.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.DeviceFingerprintHeaderName), "SubscriptionSecurity:DeviceFingerprintHeaderName is required.")
            .Validate(options => string.IsNullOrWhiteSpace(options.HmacSecret) || options.HmacSecret.Length >= 32, "SubscriptionSecurity:HmacSecret must be at least 32 characters.")
            .ValidateOnStart();
        services.PostConfigure<SubscriptionSecurityOptions>(options =>
        {
            options.HmacSecret = Environment.GetEnvironmentVariable("SUBSCRIPTION_HMAC_SECRET")
                ?? Environment.GetEnvironmentVariable("SubscriptionSecurity__HmacSecret")
                ?? options.HmacSecret;
        });
        services.AddOptions<ScanReservationOptions>()
            .Bind(configuration.GetSection(ScanReservationOptions.SectionName))
            .Validate(options => options.UnlinkedReservationTtlMinutes is >= 5 and <= 1440, "ScanReservations:UnlinkedReservationTtlMinutes must be 5-1440.")
            .Validate(options => options.ActiveJobSafetyMarginMinutes is >= 1 and <= 120, "ScanReservations:ActiveJobSafetyMarginMinutes must be 1-120.")
            .Validate(options => options.ReconciliationBatchSize is >= 1 and <= 1000, "ScanReservations:ReconciliationBatchSize must be 1-1000.")
            .ValidateOnStart();
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
            .Validate(options => options.MaxFileSizeBytes == VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes, $"VideoUpload:MaxFileSizeBytes must be {VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes}.")
            .Validate(options => options.SmartScanMaxFileSizeBytes == VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes, $"VideoUpload:SmartScanMaxFileSizeBytes must be {VideoUploadSizeLimits.AbsoluteMaxVideoSizeBytes}.")
            .Validate(options => options.DetailedScanMaxFileSizeBytes == VideoUploadSizeLimits.ProMaxVideoSizeBytes, $"VideoUpload:DetailedScanMaxFileSizeBytes must be {VideoUploadSizeLimits.ProMaxVideoSizeBytes}.")
            .Validate(options => options.UploadChunkSizeBytes is >= 5_242_880 and <= 20_971_520, "VideoUpload:UploadChunkSizeBytes must be 5-20 MB.")
            .Validate(options => options.AllowedExtensions.Length > 0 && options.AllowedContentTypes.Length > 0, "VideoUpload allowed types must be configured.")
            .Validate(options => options.MaxDurationSeconds is >= 60 and <= 86_400, "VideoUpload:MaxDurationSeconds must be 60-86400 seconds.")
            .ValidateOnStart();
        services.AddOptions<VideoStorageProtectionOptions>()
            .Bind(configuration.GetSection(VideoStorageProtectionOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.UploadTempRootPath), "VideoStorageProtection:UploadTempRootPath is required.")
            .Validate(options => options.MultipartTempSpaceMultiplier is >= 0 and <= 5, "VideoStorageProtection:MultipartTempSpaceMultiplier must be 0-5.")
            .Validate(options => options.UploadTempSpaceMultiplier is >= 1 and <= 5, "VideoStorageProtection:UploadTempSpaceMultiplier must be 1-5.")
            .Validate(options => options.LocalStorageSpaceMultiplier is >= 0 and <= 5, "VideoStorageProtection:LocalStorageSpaceMultiplier must be 0-5.")
            .Validate(options => options.ProcessingWorkingSpaceMultiplier is >= 1 and <= 10, "VideoStorageProtection:ProcessingWorkingSpaceMultiplier must be 1-10.")
            .Validate(options => options.MinimumFreeSpaceReserveBytes is >= 104_857_600 and <= 107_374_182_400, "VideoStorageProtection:MinimumFreeSpaceReserveBytes must be 100 MB-100 GB.")
            .Validate(options => options.MaxConcurrentUploads is >= 1 and <= 100, "VideoStorageProtection:MaxConcurrentUploads must be 1-100.")
            .Validate(options => options.MaxConcurrentProcessingJobs is >= 1 and <= 32, "VideoStorageProtection:MaxConcurrentProcessingJobs must be 1-32.")
            .Validate(options => options.UploadConcurrencyWaitTimeoutSeconds is >= 0 and <= 120, "VideoStorageProtection:UploadConcurrencyWaitTimeoutSeconds must be 0-120.")
            .Validate(options => options.OrphanUploadTempRetentionHours is >= 1 and <= 168, "VideoStorageProtection:OrphanUploadTempRetentionHours must be 1-168.")
            .ValidateOnStart();
        services.AddOptions<VideoProcessingOptions>()
            .Bind(configuration.GetSection(VideoProcessingOptions.SectionName))
            .Validate(options => options.SmartScanClipDurationSeconds > 0, "Smart scan clip duration must be positive.")
            .Validate(options => options.MaxSegmentCount is >= 1 and <= 100, "Max segment count must be 1-100.")
            .Validate(options => options.SegmentConcurrency is >= 1 and <= 8, "Segment concurrency must be 1-8.")
            .Validate(options => options.MaxConcurrentProviderRequests is >= 1 and <= 32, "Max concurrent provider requests must be 1-32.")
            .Validate(options => options.MaxFrameFileSizeBytes is >= 1_048_576 and <= 52_428_800, "VideoProcessing:MaxFrameFileSizeBytes must be 1-50 MB.")
            .Validate(options => options.MaxVideoWidth is >= 640 and <= 8192, "VideoProcessing:MaxVideoWidth must be 640-8192.")
            .Validate(options => options.MaxVideoHeight is >= 360 and <= 8192, "VideoProcessing:MaxVideoHeight must be 360-8192.")
            .Validate(options => options.MaxFramesPerSecond is >= 1 and <= 240, "VideoProcessing:MaxFramesPerSecond must be 1-240.")
            .Validate(options => options.MaxVideoStreams is >= 1 and <= 8, "VideoProcessing:MaxVideoStreams must be 1-8.")
            .Validate(options => options.MaxAudioStreams is >= 0 and <= 32, "VideoProcessing:MaxAudioStreams must be 0-32.")
            .Validate(options => options.MaxSubtitleStreams is >= 0 and <= 64, "VideoProcessing:MaxSubtitleStreams must be 0-64.")
            .Validate(options => options.MaxAttachmentStreams is >= 0 and <= 64, "VideoProcessing:MaxAttachmentStreams must be 0-64.")
            .Validate(options => options.MaxTotalStreams is >= 1 and <= 128, "VideoProcessing:MaxTotalStreams must be 1-128.")
            .Validate(options => options.MaxProcessOutputBytes is >= 65_536 and <= 10_485_760, "VideoProcessing:MaxProcessOutputBytes must be 64 KB-10 MB.")
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
        services.AddOptions<R2Options>()
            .Bind(configuration.GetSection(R2Options.SectionName))
            .Validate(options => !IsR2StorageSelected(configuration) || !string.IsNullOrWhiteSpace(ResolveR2ServiceUrl(options)), "R2:ServiceUrl or R2:AccountId is required when Storage:Provider is R2.")
            .Validate(options => !IsR2StorageSelected(configuration) || !string.IsNullOrWhiteSpace(options.BucketName), "R2:BucketName is required when Storage:Provider is R2.")
            .Validate(options => !IsR2StorageSelected(configuration) || !string.IsNullOrWhiteSpace(options.AccessKeyId), "R2:AccessKeyId is required when Storage:Provider is R2.")
            .Validate(options => !IsR2StorageSelected(configuration) || !string.IsNullOrWhiteSpace(options.SecretAccessKey), "R2:SecretAccessKey is required when Storage:Provider is R2.")
            .ValidateOnStart();
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
        services.AddHttpContextAccessor();
        services.AddSingleton<IMonitoringAlertService, LoggingMonitoringAlertService>();
        services.AddSingleton<IApplicationEncryptionService, AesApplicationEncryptionService>();
        services.AddScoped<IHashPseudonymizationService, HmacPseudonymizationService>();
        services.AddScoped<IClientIpResolver, ClientIpResolver>();
        services.AddScoped<IDeviceIdentityService, DeviceIdentityService>();
        services.AddScoped<IEntitlementService, EntitlementService>();
        services.AddScoped<IScanReservationReconciliationService, ScanReservationReconciliationService>();
        services.AddScoped<IPaymentGateway, EasypaisaPaymentGateway>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IAuthThrottleService, InMemoryAuthThrottleService>();
        services.AddScoped<IPasswordResetEmailSender, SmtpAuthEmailSender>();
        services.AddScoped<IEmailConfirmationSender, SmtpAuthEmailSender>();
        if (IsR2StorageSelected(configuration))
        {
            services.AddSingleton<IAmazonS3>(serviceProvider =>
            {
                var r2Options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<R2Options>>().Value;
                var config = new AmazonS3Config
                {
                    ServiceURL = ResolveR2ServiceUrl(r2Options),
                    ForcePathStyle = true,
                    AuthenticationRegion = string.IsNullOrWhiteSpace(r2Options.Region) ? "auto" : r2Options.Region
                };
                return new AmazonS3Client(
                    new BasicAWSCredentials(r2Options.AccessKeyId, r2Options.SecretAccessKey),
                    config);
            });
            services.AddScoped<IObjectStorageService, R2ObjectStorageService>();
        }
        else
        {
            services.AddScoped<IObjectStorageService, LocalObjectStorageService>();
        }
        services.AddScoped<IVideoService, VideoService>();
        services.AddScoped<IVideoStorageCapacityService, DiskVideoStorageCapacityService>();
        services.AddSingleton<IVideoWorkloadGate, VideoWorkloadGate>();
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

    private static bool IsR2StorageSelected(IConfiguration configuration)
    {
        var provider = configuration.GetSection(LocalStorageOptions.SectionName).Get<LocalStorageOptions>()?.Provider;
        return string.Equals(provider, "R2", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveR2ServiceUrl(R2Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        {
            return options.ServiceUrl.TrimEnd('/');
        }

        return string.IsNullOrWhiteSpace(options.AccountId)
            ? string.Empty
            : $"https://{options.AccountId}.r2.cloudflarestorage.com";
    }
}
