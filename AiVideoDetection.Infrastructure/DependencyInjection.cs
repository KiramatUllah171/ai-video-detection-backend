using AiVideoDetection.Application.Auth.Interfaces;
using AiVideoDetection.Application.Videos.Interfaces;
using AiVideoDetection.Application.Videos.Options;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Auth;
using AiVideoDetection.Infrastructure.Data;
using AiVideoDetection.Infrastructure.Storage;
using AiVideoDetection.Infrastructure.Videos;
using AiVideoDetection.Infrastructure.Videos.Ai;
using AiVideoDetection.Infrastructure.Videos.Matching;
using AiVideoDetection.Infrastructure.Videos.Processing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
        services.Configure<VideoUploadOptions>(configuration.GetSection(VideoUploadOptions.SectionName));
        services.Configure<VideoProcessingOptions>(configuration.GetSection(VideoProcessingOptions.SectionName));
        services.Configure<AiServiceOptions>(configuration.GetSection(AiServiceOptions.SectionName));
        services.PostConfigure<AiServiceOptions>(options =>
        {
            options.ProviderMode = Environment.GetEnvironmentVariable("AI_PROVIDER") ?? options.ProviderMode;
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
        });
        services.Configure<ScoringOptions>(configuration.GetSection(ScoringOptions.SectionName));
        services.Configure<InternalMatchingOptions>(configuration.GetSection(InternalMatchingOptions.SectionName));
        services.Configure<LocalStorageOptions>(configuration.GetSection(LocalStorageOptions.SectionName));
        services.AddHttpClient("AiService");

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IObjectStorageService, LocalObjectStorageService>();
        services.AddScoped<IVideoService, VideoService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IAnalysisJobQueue, HangfireAnalysisJobQueue>();
        services.AddScoped<IVideoProcessingService, VideoProcessingService>();
        services.AddScoped<IFrameExtractionService, FrameExtractionService>();
        services.AddScoped<IMetadataExtractionService, MetadataExtractionService>();
        services.AddScoped<IJobLogService, JobLogService>();
        services.AddScoped<IProcessRunner, ProcessRunner>();
        services.AddScoped<IFfmpegToolLocator, FfmpegToolLocator>();
        services.AddScoped<IVideoProcessingToolValidator, VideoProcessingToolValidator>();
        services.AddScoped<IAiInferenceClient, PythonAiInferenceClient>();
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
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });
    }
}
