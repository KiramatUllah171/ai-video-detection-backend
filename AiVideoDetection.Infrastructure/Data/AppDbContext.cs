using AiVideoDetection.Domain.Entities;
using AiVideoDetection.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql.NameTranslation;

namespace AiVideoDetection.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    private static readonly NpgsqlNullNameTranslator EnumNameTranslator = new();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Video> Videos => Set<Video>();

    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();

    public DbSet<JobLog> JobLogs => Set<JobLog>();

    public DbSet<VideoFrame> VideoFrames => Set<VideoFrame>();

    public DbSet<MetadataResult> MetadataResults => Set<MetadataResult>();

    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    public DbSet<AiResult> AiResults => Set<AiResult>();

    public DbSet<EvidenceItem> EvidenceItems => Set<EvidenceItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresEnum<UserRole>(
            schema: null,
            name: "user_role",
            nameTranslator: EnumNameTranslator);
        modelBuilder.HasPostgresEnum<VideoStatus>(
            schema: null,
            name: "video_status",
            nameTranslator: EnumNameTranslator);
        modelBuilder.HasPostgresEnum<JobStatus>(
            schema: null,
            name: "job_status",
            nameTranslator: EnumNameTranslator);
        modelBuilder.HasPostgresEnum<AnalysisLabel>(
            schema: null,
            name: "analysis_label",
            nameTranslator: EnumNameTranslator);
        modelBuilder.HasPostgresEnum<EvidenceType>(
            schema: null,
            name: "evidence_type",
            nameTranslator: EnumNameTranslator);
        modelBuilder.HasPostgresEnum<EvidenceSeverity>(
            schema: null,
            name: "evidence_severity",
            nameTranslator: EnumNameTranslator);

        ConfigureUser(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigureVideo(modelBuilder);
        ConfigureAnalysisJob(modelBuilder);
        ConfigureJobLog(modelBuilder);
        ConfigureVideoFrame(modelBuilder);
        ConfigureMetadataResult(modelBuilder);
        ConfigureModelVersion(modelBuilder);
        ConfigureAiResult(modelBuilder);
        ConfigureEvidenceItem(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                if (entry.Entity.UpdatedAt == default)
                {
                    entry.Entity.UpdatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(user => user.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<RefreshToken>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<Video>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                if (entry.Entity.UpdatedAt == default)
                {
                    entry.Entity.UpdatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(video => video.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AnalysisJob>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                if (entry.Entity.UpdatedAt == default)
                {
                    entry.Entity.UpdatedAt = now;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(job => job.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<JobLog>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<VideoFrame>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<MetadataResult>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ModelVersion>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AiResult>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<EvidenceItem>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(user => user.Id);

            entity.Property(user => user.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(user => user.Name)
                .HasColumnName("name")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(user => user.Email)
                .HasColumnName("email")
                .HasMaxLength(320)
                .IsRequired();

            entity.Property(user => user.PasswordHash)
                .HasColumnName("password_hash")
                .IsRequired();

            entity.Property(user => user.Role)
                .HasColumnName("role")
                .HasColumnType("user_role")
                .HasSentinel((UserRole)(-1))
                .HasDefaultValueSql("'User'::user_role")
                .IsRequired();

            entity.Property(user => user.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true)
                .IsRequired();

            entity.Property(user => user.EmailConfirmed)
                .HasColumnName("email_confirmed")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(user => user.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.Property(user => user.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasIndex(user => user.Email)
                .IsUnique()
                .HasDatabaseName("ix_users_email");
        });
    }

    private static void ConfigureRefreshToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");

            entity.HasKey(refreshToken => refreshToken.Id);

            entity.Property(refreshToken => refreshToken.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(refreshToken => refreshToken.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(refreshToken => refreshToken.TokenHash)
                .HasColumnName("token_hash")
                .IsRequired();

            entity.Property(refreshToken => refreshToken.ExpiresAt)
                .HasColumnName("expires_at")
                .IsRequired();

            entity.Property(refreshToken => refreshToken.RevokedAt)
                .HasColumnName("revoked_at");

            entity.Property(refreshToken => refreshToken.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.Property(refreshToken => refreshToken.CreatedByIp)
                .HasColumnName("created_by_ip")
                .HasColumnType("inet");

            entity.HasOne(refreshToken => refreshToken.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(refreshToken => refreshToken.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(refreshToken => refreshToken.UserId)
                .HasDatabaseName("ix_refresh_tokens_user_id");
        });
    }

    private static void ConfigureVideo(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Video>(entity =>
        {
            entity.ToTable("videos");

            entity.HasKey(video => video.Id);

            entity.Property(video => video.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(video => video.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(video => video.OriginalName)
                .HasColumnName("original_name")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(video => video.FileUrl)
                .HasColumnName("file_url")
                .IsRequired();

            entity.Property(video => video.ThumbnailUrl)
                .HasColumnName("thumbnail_url");

            entity.Property(video => video.ContentType)
                .HasColumnName("content_type")
                .HasMaxLength(100);

            entity.Property(video => video.FileExtension)
                .HasColumnName("file_extension")
                .HasMaxLength(20);

            entity.Property(video => video.FileSize)
                .HasColumnName("file_size")
                .IsRequired();

            entity.Property(video => video.DurationSeconds)
                .HasColumnName("duration_seconds");

            entity.Property(video => video.FormatName)
                .HasColumnName("format_name")
                .HasMaxLength(100);

            entity.Property(video => video.Sha256Hash)
                .HasColumnName("sha256_hash")
                .HasMaxLength(128);

            entity.Property(video => video.Status)
                .HasColumnName("status")
                .HasColumnType("video_status")
                .HasSentinel((VideoStatus)(-1))
                .HasDefaultValueSql("'Uploaded'::video_status")
                .IsRequired();

            entity.Property(video => video.RetentionDeleteAt)
                .HasColumnName("retention_delete_at");

            entity.Property(video => video.DeletedAt)
                .HasColumnName("deleted_at");

            entity.Property(video => video.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.Property(video => video.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(video => video.User)
                .WithMany(user => user.Videos)
                .HasForeignKey(video => video.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(video => new { video.UserId, video.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_videos_user_created");

            entity.HasIndex(video => video.Status)
                .HasDatabaseName("ix_videos_status");

            entity.HasIndex(video => video.Sha256Hash)
                .HasDatabaseName("ix_videos_sha256_hash");
        });
    }

    private static void ConfigureAnalysisJob(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisJob>(entity =>
        {
            entity.ToTable("analysis_jobs", table =>
            {
                table.HasCheckConstraint("ck_analysis_jobs_progress", "progress >= 0 AND progress <= 100");
            });

            entity.HasKey(job => job.Id);

            entity.Property(job => job.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(job => job.VideoId)
                .HasColumnName("video_id")
                .IsRequired();

            entity.Property(job => job.Status)
                .HasColumnName("status")
                .HasColumnType("job_status")
                .HasSentinel((JobStatus)(-1))
                .HasDefaultValueSql("'Queued'::job_status")
                .IsRequired();

            entity.Property(job => job.Progress)
                .HasColumnName("progress")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(job => job.CurrentStep)
                .HasColumnName("current_step")
                .HasMaxLength(200);

            entity.Property(job => job.ErrorMessage)
                .HasColumnName("error_message");

            entity.Property(job => job.ErrorCode)
                .HasColumnName("error_code")
                .HasMaxLength(100);

            entity.Property(job => job.RetryCount)
                .HasColumnName("retry_count")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(job => job.MaxRetryCount)
                .HasColumnName("max_retry_count")
                .HasDefaultValue(3)
                .IsRequired();

            entity.Property(job => job.StartedAt)
                .HasColumnName("started_at");

            entity.Property(job => job.CompletedAt)
                .HasColumnName("completed_at");

            entity.Property(job => job.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.Property(job => job.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(job => job.Video)
                .WithMany(video => video.AnalysisJobs)
                .HasForeignKey(job => job.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(job => job.VideoId)
                .HasDatabaseName("ix_analysis_jobs_video_id");

            entity.HasIndex(job => job.Status)
                .HasDatabaseName("ix_analysis_jobs_status");
        });
    }

    private static void ConfigureJobLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<JobLog>(entity =>
        {
            entity.ToTable("job_logs");

            entity.HasKey(log => log.Id);

            entity.Property(log => log.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(log => log.JobId)
                .HasColumnName("job_id")
                .IsRequired();

            entity.Property(log => log.StepName)
                .HasColumnName("step_name")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(log => log.Level)
                .HasColumnName("level")
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(log => log.Message)
                .HasColumnName("message")
                .IsRequired();

            entity.Property(log => log.DetailsJson)
                .HasColumnName("details_json")
                .HasColumnType("jsonb");

            entity.Property(log => log.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(log => log.AnalysisJob)
                .WithMany(job => job.Logs)
                .HasForeignKey(log => log.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(log => new { log.JobId, log.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_job_logs_job_id_created");
        });
    }

    private static void ConfigureVideoFrame(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VideoFrame>(entity =>
        {
            entity.ToTable("video_frames");

            entity.HasKey(frame => frame.Id);

            entity.Property(frame => frame.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(frame => frame.VideoId)
                .HasColumnName("video_id")
                .IsRequired();

            entity.Property(frame => frame.FrameUrl)
                .HasColumnName("frame_url")
                .IsRequired();

            entity.Property(frame => frame.TimestampSeconds)
                .HasColumnName("timestamp_seconds")
                .IsRequired();

            entity.Property(frame => frame.FrameIndex)
                .HasColumnName("frame_index")
                .IsRequired();

            entity.Property(frame => frame.Width)
                .HasColumnName("width");

            entity.Property(frame => frame.Height)
                .HasColumnName("height");

            entity.Property(frame => frame.IsKeyframe)
                .HasColumnName("is_keyframe")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(frame => frame.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(frame => frame.Video)
                .WithMany(video => video.Frames)
                .HasForeignKey(frame => frame.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(frame => frame.VideoId)
                .HasDatabaseName("ix_video_frames_video_id");

            entity.HasIndex(frame => new { frame.VideoId, frame.FrameIndex })
                .IsUnique()
                .HasDatabaseName("ux_video_frames_video_id_frame_index");
        });
    }

    private static void ConfigureMetadataResult(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetadataResult>(entity =>
        {
            entity.ToTable("metadata_results");

            entity.HasKey(metadata => metadata.Id);

            entity.Property(metadata => metadata.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(metadata => metadata.VideoId)
                .HasColumnName("video_id")
                .IsRequired();

            entity.Property(metadata => metadata.Codec)
                .HasColumnName("codec")
                .HasMaxLength(100);

            entity.Property(metadata => metadata.AudioCodec)
                .HasColumnName("audio_codec")
                .HasMaxLength(100);

            entity.Property(metadata => metadata.Fps)
                .HasColumnName("fps");

            entity.Property(metadata => metadata.Resolution)
                .HasColumnName("resolution")
                .HasMaxLength(50);

            entity.Property(metadata => metadata.DurationSeconds)
                .HasColumnName("duration_seconds");

            entity.Property(metadata => metadata.Bitrate)
                .HasColumnName("bitrate");

            entity.Property(metadata => metadata.Encoder)
                .HasColumnName("encoder");

            entity.Property(metadata => metadata.CreationTime)
                .HasColumnName("creation_time");

            entity.Property(metadata => metadata.HasMissingMetadata)
                .HasColumnName("has_missing_metadata")
                .IsRequired();

            entity.Property(metadata => metadata.WarningsJson)
                .HasColumnName("warnings_json")
                .HasColumnType("jsonb");

            entity.Property(metadata => metadata.RawJson)
                .HasColumnName("raw_json")
                .HasColumnType("jsonb");

            entity.Property(metadata => metadata.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(metadata => metadata.Video)
                .WithOne(video => video.MetadataResult)
                .HasForeignKey<MetadataResult>(metadata => metadata.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(metadata => metadata.VideoId)
                .IsUnique()
                .HasDatabaseName("ux_metadata_results_video_id");
        });
    }

    private static void ConfigureModelVersion(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ModelVersion>(entity =>
        {
            entity.ToTable("model_versions");
            entity.HasKey(model => model.Id);

            entity.Property(model => model.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(model => model.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            entity.Property(model => model.Version).HasColumnName("version").HasMaxLength(100).IsRequired();
            entity.Property(model => model.Description).HasColumnName("description");
            entity.Property(model => model.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
            entity.Property(model => model.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").IsRequired();

            entity.HasIndex(model => model.IsActive).HasDatabaseName("ix_model_versions_active");
            entity.HasIndex(model => model.Version).IsUnique().HasDatabaseName("ux_model_versions_version");

            entity.HasData(new ModelVersion
            {
                Id = 1,
                Name = "Mock Video AI",
                Version = "mock-video-ai-v1",
                Description = "Mock deterministic AI scoring model for pipeline integration.",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero)
            });
        });
    }

    private static void ConfigureAiResult(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiResult>(entity =>
        {
            entity.ToTable("ai_results");
            entity.HasKey(result => result.Id);

            entity.Property(result => result.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(result => result.VideoId).HasColumnName("video_id").IsRequired();
            entity.Property(result => result.ModelVersionId).HasColumnName("model_version_id");
            entity.Property(result => result.VisualScore).HasColumnName("visual_score").IsRequired();
            entity.Property(result => result.TemporalScore).HasColumnName("temporal_score");
            entity.Property(result => result.MetadataScore).HasColumnName("metadata_score");
            entity.Property(result => result.FinalScore).HasColumnName("final_score").IsRequired();
            entity.Property(result => result.Confidence).HasColumnName("confidence").IsRequired();
            entity.Property(result => result.Label)
                .HasColumnName("label")
                .HasColumnType("analysis_label")
                .HasSentinel((AnalysisLabel)(-1))
                .IsRequired();
            entity.Property(result => result.RawModelOutputJson)
                .HasColumnName("raw_model_output_json")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(result => result.Summary).HasColumnName("summary").HasMaxLength(1000);
            entity.Property(result => result.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").IsRequired();

            entity.HasOne(result => result.Video)
                .WithMany(video => video.AiResults)
                .HasForeignKey(result => result.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(result => result.ModelVersion)
                .WithMany(model => model.AiResults)
                .HasForeignKey(result => result.ModelVersionId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(result => new { result.VideoId, result.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_ai_results_video_id_created");
            entity.HasIndex(result => result.Label).HasDatabaseName("ix_ai_results_label");
            entity.HasIndex(result => result.ModelVersionId).HasDatabaseName("ix_ai_results_model_version_id");
        });
    }

    private static void ConfigureEvidenceItem(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvidenceItem>(entity =>
        {
            entity.ToTable("evidence_items");
            entity.HasKey(evidence => evidence.Id);

            entity.Property(evidence => evidence.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(evidence => evidence.AiResultId).HasColumnName("ai_result_id").IsRequired();
            entity.Property(evidence => evidence.VideoFrameId).HasColumnName("video_frame_id");
            entity.Property(evidence => evidence.Type)
                .HasColumnName("type")
                .HasColumnType("evidence_type")
                .HasSentinel((EvidenceType)(-1))
                .IsRequired();
            entity.Property(evidence => evidence.Severity)
                .HasColumnName("severity")
                .HasColumnType("evidence_severity")
                .HasSentinel((EvidenceSeverity)(-1))
                .IsRequired();
            entity.Property(evidence => evidence.Title).HasColumnName("title").HasMaxLength(300).IsRequired();
            entity.Property(evidence => evidence.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
            entity.Property(evidence => evidence.ScoreImpact).HasColumnName("score_impact");
            entity.Property(evidence => evidence.TimestampSeconds).HasColumnName("timestamp_seconds");
            entity.Property(evidence => evidence.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").IsRequired();

            entity.HasOne(evidence => evidence.AiResult)
                .WithMany(result => result.EvidenceItems)
                .HasForeignKey(evidence => evidence.AiResultId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(evidence => evidence.VideoFrame)
                .WithMany(frame => frame.EvidenceItems)
                .HasForeignKey(evidence => evidence.VideoFrameId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasIndex(evidence => evidence.AiResultId).HasDatabaseName("ix_evidence_items_ai_result_id");
            entity.HasIndex(evidence => evidence.VideoFrameId).HasDatabaseName("ix_evidence_items_video_frame_id");
            entity.HasIndex(evidence => evidence.Severity).HasDatabaseName("ix_evidence_items_severity");
        });
    }
}
