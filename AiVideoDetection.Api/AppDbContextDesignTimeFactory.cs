using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.NameTranslation;

namespace AiVideoDetection.Api;

public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private static readonly NpgsqlNullNameTranslator EnumNameTranslator = new();

    public AppDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=ai_video_detection_db;Username=postgres";
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MapEnum<UserRole>("user_role", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MapEnum<VideoStatus>("video_status", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MapEnum<JobStatus>("job_status", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MapEnum<AnalysisSegmentStatus>("analysis_segment_status", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MapEnum<AnalysisLabel>("analysis_label", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MapEnum<EvidenceType>("evidence_type", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MapEnum<EvidenceSeverity>("evidence_severity", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MapEnum<ConfidenceLevel>("confidence_level", nameTranslator: EnumNameTranslator);
                npgsqlOptions.MigrationsAssembly("AiVideoDetection.Api");
            });

        return new AppDbContext(optionsBuilder.Options);
    }
}
