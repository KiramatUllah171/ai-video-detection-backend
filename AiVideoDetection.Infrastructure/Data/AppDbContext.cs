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

    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();

    public DbSet<EmailConfirmationToken> EmailConfirmationTokens => Set<EmailConfirmationToken>();

    public DbSet<Video> Videos => Set<Video>();

    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();

    public DbSet<AnalysisSegment> AnalysisSegments => Set<AnalysisSegment>();

    public DbSet<JobLog> JobLogs => Set<JobLog>();

    public DbSet<VideoFrame> VideoFrames => Set<VideoFrame>();

    public DbSet<MetadataResult> MetadataResults => Set<MetadataResult>();

    public DbSet<ModelVersion> ModelVersions => Set<ModelVersion>();

    public DbSet<AiResult> AiResults => Set<AiResult>();

    public DbSet<EvidenceItem> EvidenceItems => Set<EvidenceItem>();

    public DbSet<FrameHash> FrameHashes => Set<FrameHash>();

    public DbSet<SourceMatch> SourceMatches => Set<SourceMatch>();

    public DbSet<AiProviderRequest> AiProviderRequests => Set<AiProviderRequest>();

    public DbSet<ApiUsageMonthly> ApiUsageMonthly => Set<ApiUsageMonthly>();

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
        modelBuilder.HasPostgresEnum<AnalysisSegmentStatus>(
            schema: null,
            name: "analysis_segment_status",
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
        modelBuilder.HasPostgresEnum<ConfidenceLevel>(
            schema: null,
            name: "confidence_level",
            nameTranslator: EnumNameTranslator);

        ConfigureUser(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigurePasswordResetToken(modelBuilder);
        ConfigureEmailConfirmationToken(modelBuilder);
        ConfigureVideo(modelBuilder);
        ConfigureAnalysisJob(modelBuilder);
        ConfigureAnalysisSegment(modelBuilder);
        ConfigureJobLog(modelBuilder);
        ConfigureVideoFrame(modelBuilder);
        ConfigureMetadataResult(modelBuilder);
        ConfigureModelVersion(modelBuilder);
        ConfigureAiResult(modelBuilder);
        ConfigureEvidenceItem(modelBuilder);
        ConfigureFrameHash(modelBuilder);
        ConfigureSourceMatch(modelBuilder);
        ConfigureAiProviderRequest(modelBuilder);
        ConfigureApiUsageMonthly(modelBuilder);
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

        foreach (var entry in ChangeTracker.Entries<PasswordResetToken>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<EmailConfirmationToken>())
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

        foreach (var entry in ChangeTracker.Entries<AnalysisSegment>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }

                entry.Entity.LastActivityAt ??= now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Property(segment => segment.CreatedAt).IsModified = false;
                entry.Entity.LastActivityAt ??= now;
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

        foreach (var entry in ChangeTracker.Entries<FrameHash>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<SourceMatch>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<AiProviderRequest>())
        {
            if (entry.State == EntityState.Added && entry.Entity.CreatedAt == default)
            {
                entry.Entity.CreatedAt = now;
            }
        }

        foreach (var entry in ChangeTracker.Entries<ApiUsageMonthly>())
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
                entry.Property(usage => usage.CreatedAt).IsModified = false;
                entry.Entity.UpdatedAt = now;
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

    private static void ConfigurePasswordResetToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.ToTable("password_reset_tokens");

            entity.HasKey(token => token.Id);

            entity.Property(token => token.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(token => token.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(token => token.TokenHash)
                .HasColumnName("token_hash")
                .IsRequired();

            entity.Property(token => token.ExpiresAt)
                .HasColumnName("expires_at")
                .IsRequired();

            entity.Property(token => token.UsedAt)
                .HasColumnName("used_at");

            entity.Property(token => token.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(token => token.User)
                .WithMany(user => user.PasswordResetTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(token => token.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_password_reset_tokens_token_hash");

            entity.HasIndex(token => new { token.UserId, token.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_password_reset_tokens_user_created");
        });
    }

    private static void ConfigureEmailConfirmationToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailConfirmationToken>(entity =>
        {
            entity.ToTable("email_confirmation_tokens");

            entity.HasKey(token => token.Id);

            entity.Property(token => token.Id)
                .HasColumnName("id")
                .UseIdentityByDefaultColumn();

            entity.Property(token => token.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(token => token.TokenHash)
                .HasColumnName("token_hash")
                .IsRequired();

            entity.Property(token => token.ExpiresAt)
                .HasColumnName("expires_at")
                .IsRequired();

            entity.Property(token => token.UsedAt)
                .HasColumnName("used_at");

            entity.Property(token => token.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(token => token.User)
                .WithMany(user => user.EmailConfirmationTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(token => token.TokenHash)
                .IsUnique()
                .HasDatabaseName("ux_email_confirmation_tokens_token_hash");

            entity.HasIndex(token => new { token.UserId, token.CreatedAt })
                .IsDescending(false, true)
                .HasDatabaseName("ix_email_confirmation_tokens_user_created");
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

            entity.Property(job => job.FailedStage)
                .HasColumnName("failed_stage")
                .HasMaxLength(100);

            entity.Property(job => job.FailedAt)
                .HasColumnName("failed_at");

            entity.Property(job => job.CancelRequested)
                .HasColumnName("cancel_requested")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(job => job.CancelRequestedAt)
                .HasColumnName("cancel_requested_at");

            entity.Property(job => job.PauseRequested)
                .HasColumnName("pause_requested")
                .HasDefaultValue(false)
                .IsRequired();

            entity.Property(job => job.PauseRequestedAt)
                .HasColumnName("pause_requested_at");

            entity.Property(job => job.PausedAt)
                .HasColumnName("paused_at");

            entity.Property(job => job.ResumedAt)
                .HasColumnName("resumed_at");

            entity.Property(job => job.PausedFromStage)
                .HasColumnName("paused_from_stage")
                .HasMaxLength(200);

            entity.Property(job => job.LastCheckpoint)
                .HasColumnName("last_checkpoint")
                .HasMaxLength(200);

            entity.Property(job => job.ResumeBackgroundJobId)
                .HasColumnName("resume_background_job_id")
                .HasMaxLength(100);

            entity.Property(job => job.ScanMode)
                .HasColumnName("scan_mode")
                .HasMaxLength(50);

            entity.Property(job => job.CompletedSegments)
                .HasColumnName("completed_segments")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(job => job.TotalSegments)
                .HasColumnName("total_segments")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(job => job.AnalyzedCoverageSeconds)
                .HasColumnName("analyzed_coverage_seconds");

            entity.Property(job => job.TotalDurationSeconds)
                .HasColumnName("total_duration_seconds");

            entity.Property(job => job.LastActivityAt)
                .HasColumnName("last_activity_at");

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

            entity.HasIndex(job => job.VideoId)
                .IsUnique()
                .HasFilter("status NOT IN ('Completed', 'Failed', 'Cancelled')")
                .HasDatabaseName("ux_analysis_jobs_one_active_per_video");
        });
    }

    private static void ConfigureAnalysisSegment(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AnalysisSegment>(entity =>
        {
            entity.ToTable("analysis_segments", table =>
            {
                table.HasCheckConstraint("ck_analysis_segments_progress", "progress >= 0 AND progress <= 100");
                table.HasCheckConstraint("ck_analysis_segments_times", "end_time > start_time AND duration > 0");
            });

            entity.HasKey(segment => segment.Id);
            entity.Property(segment => segment.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(segment => segment.AnalysisJobId).HasColumnName("analysis_job_id").IsRequired();
            entity.Property(segment => segment.VideoId).HasColumnName("video_id").IsRequired();
            entity.Property(segment => segment.SegmentIndex).HasColumnName("segment_index").IsRequired();
            entity.Property(segment => segment.StartTime).HasColumnName("start_time").IsRequired();
            entity.Property(segment => segment.EndTime).HasColumnName("end_time").IsRequired();
            entity.Property(segment => segment.Duration).HasColumnName("duration").IsRequired();
            entity.Property(segment => segment.LocalTemporaryPath).HasColumnName("local_temporary_path");
            entity.Property(segment => segment.Status)
                .HasColumnName("status")
                .HasColumnType("analysis_segment_status")
                .HasSentinel((AnalysisSegmentStatus)(-1))
                .HasDefaultValueSql("'Pending'::analysis_segment_status")
                .IsRequired();
            entity.Property(segment => segment.Progress).HasColumnName("progress").HasDefaultValue(0).IsRequired();
            entity.Property(segment => segment.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0).IsRequired();
            entity.Property(segment => segment.ProviderRequestId).HasColumnName("provider_request_id").HasMaxLength(200);
            entity.Property(segment => segment.AiScore).HasColumnName("ai_score");
            entity.Property(segment => segment.Confidence).HasColumnName("confidence");
            entity.Property(segment => segment.ResultJson).HasColumnName("result_json").HasColumnType("jsonb");
            entity.Property(segment => segment.ErrorCode).HasColumnName("error_code").HasMaxLength(100);
            entity.Property(segment => segment.SafeErrorMessage).HasColumnName("safe_error_message");
            entity.Property(segment => segment.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").IsRequired();
            entity.Property(segment => segment.StartedAt).HasColumnName("started_at");
            entity.Property(segment => segment.CompletedAt).HasColumnName("completed_at");
            entity.Property(segment => segment.FailedAt).HasColumnName("failed_at");
            entity.Property(segment => segment.LastActivityAt).HasColumnName("last_activity_at");

            entity.HasOne(segment => segment.AnalysisJob)
                .WithMany(job => job.Segments)
                .HasForeignKey(segment => segment.AnalysisJobId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(segment => segment.Video)
                .WithMany()
                .HasForeignKey(segment => segment.VideoId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(segment => new { segment.AnalysisJobId, segment.SegmentIndex })
                .IsUnique()
                .HasDatabaseName("ux_analysis_segments_job_index");
            entity.HasIndex(segment => segment.Status).HasDatabaseName("ix_analysis_segments_status");
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
            entity.Property(result => result.Provider).HasColumnName("provider").HasMaxLength(100).HasDefaultValue("Local").IsRequired();
            entity.Property(result => result.ProviderMode).HasColumnName("provider_mode").HasMaxLength(50).HasDefaultValue("local").IsRequired();
            entity.Property(result => result.FinalDecisionSource).HasColumnName("final_decision_source").HasMaxLength(50).HasDefaultValue("Local").IsRequired();
            entity.Property(result => result.ExternalProviderName).HasColumnName("external_provider_name").HasMaxLength(100);
            entity.Property(result => result.ExternalProviderResultId).HasColumnName("external_provider_result_id").HasMaxLength(200);
            entity.Property(result => result.ExternalProviderJobId).HasColumnName("external_provider_job_id").HasMaxLength(200);
            entity.Property(result => result.ExternalProviderStatus).HasColumnName("external_provider_status").HasMaxLength(100);
            entity.Property(result => result.ExternalScore).HasColumnName("external_score");
            entity.Property(result => result.ExternalConfidence).HasColumnName("external_confidence");
            entity.Property(result => result.ExternalLabel).HasColumnName("external_label").HasMaxLength(100);
            entity.Property(result => result.ExternalRawResponseJson).HasColumnName("external_raw_response_json").HasColumnType("jsonb");
            entity.Property(result => result.ExternalErrorMessage).HasColumnName("external_error_message");
            entity.Property(result => result.ExternalRequestedAt).HasColumnName("external_requested_at");
            entity.Property(result => result.ExternalCompletedAt).HasColumnName("external_completed_at");
            entity.Property(result => result.FallbackUsed).HasColumnName("fallback_used").HasDefaultValue(false).IsRequired();
            entity.Property(result => result.FallbackReason).HasColumnName("fallback_reason");
            entity.Property(result => result.LocalResultJson).HasColumnName("local_result_json").HasColumnType("jsonb");
            entity.Property(result => result.HybridResultJson).HasColumnName("hybrid_result_json").HasColumnType("jsonb");
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
            entity.HasIndex(result => result.Provider).HasDatabaseName("ix_ai_results_provider");
        });
    }

    private static void ConfigureAiProviderRequest(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiProviderRequest>(entity =>
        {
            entity.ToTable("ai_provider_requests");
            entity.HasKey(request => request.Id);
            entity.Property(request => request.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(request => request.VideoId).HasColumnName("video_id").IsRequired();
            entity.Property(request => request.AnalysisJobId).HasColumnName("analysis_job_id").IsRequired();
            entity.Property(request => request.AiResultId).HasColumnName("ai_result_id");
            entity.Property(request => request.UserId).HasColumnName("user_id").IsRequired();
            entity.Property(request => request.ProviderName).HasColumnName("provider_name").HasMaxLength(100).IsRequired();
            entity.Property(request => request.ProviderMode).HasColumnName("provider_mode").HasMaxLength(50).IsRequired();
            entity.Property(request => request.ProviderRequestId).HasColumnName("provider_request_id").HasMaxLength(200);
            entity.Property(request => request.ProviderJobId).HasColumnName("provider_job_id").HasMaxLength(200);
            entity.Property(request => request.Status).HasColumnName("status").HasMaxLength(100).IsRequired();
            entity.Property(request => request.RequestStartedAt).HasColumnName("request_started_at").IsRequired();
            entity.Property(request => request.RequestCompletedAt).HasColumnName("request_completed_at");
            entity.Property(request => request.DurationMs).HasColumnName("duration_ms");
            entity.Property(request => request.HttpStatusCode).HasColumnName("http_status_code");
            entity.Property(request => request.ErrorMessage).HasColumnName("error_message");
            entity.Property(request => request.RawRequestMetadataJson).HasColumnName("raw_request_metadata_json").HasColumnType("jsonb");
            entity.Property(request => request.RawResponseJson).HasColumnName("raw_response_json").HasColumnType("jsonb");
            entity.Property(request => request.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").IsRequired();

            entity.HasOne(request => request.Video).WithMany().HasForeignKey(request => request.VideoId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(request => request.AnalysisJob).WithMany().HasForeignKey(request => request.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(request => request.AiResult).WithMany(result => result.ProviderRequests).HasForeignKey(request => request.AiResultId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(request => request.User).WithMany().HasForeignKey(request => request.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(request => new { request.VideoId, request.CreatedAt }).IsDescending(false, true).HasDatabaseName("ix_ai_provider_requests_video_created");
            entity.HasIndex(request => new { request.ProviderName, request.ProviderJobId }).HasDatabaseName("ix_ai_provider_requests_provider_job");
        });
    }

    private static void ConfigureApiUsageMonthly(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiUsageMonthly>(entity =>
        {
            entity.ToTable("api_usage_monthly");
            entity.HasKey(usage => usage.Id);
            entity.Property(usage => usage.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(usage => usage.ProviderName).HasColumnName("provider_name").HasMaxLength(100).IsRequired();
            entity.Property(usage => usage.Year).HasColumnName("year").IsRequired();
            entity.Property(usage => usage.Month).HasColumnName("month").IsRequired();
            entity.Property(usage => usage.RequestCount).HasColumnName("request_count").HasDefaultValue(0).IsRequired();
            entity.Property(usage => usage.SuccessCount).HasColumnName("success_count").HasDefaultValue(0).IsRequired();
            entity.Property(usage => usage.FailedCount).HasColumnName("failed_count").HasDefaultValue(0).IsRequired();
            entity.Property(usage => usage.QuotaLimit).HasColumnName("quota_limit").IsRequired();
            entity.Property(usage => usage.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()").IsRequired();
            entity.Property(usage => usage.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()").IsRequired();
            entity.HasIndex(usage => new { usage.ProviderName, usage.Year, usage.Month }).IsUnique().HasDatabaseName("ux_api_usage_monthly_provider_year_month");
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

    private static void ConfigureFrameHash(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FrameHash>(entity =>
        {
            entity.ToTable("frame_hashes");
            entity.HasKey(hash => hash.Id);

            entity.Property(hash => hash.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(hash => hash.VideoId).HasColumnName("video_id").IsRequired();
            entity.Property(hash => hash.FrameId).HasColumnName("frame_id").IsRequired();
            entity.Property(hash => hash.PHash).HasColumnName("phash").HasMaxLength(128);
            entity.Property(hash => hash.DHash).HasColumnName("dhash").HasMaxLength(128);
            entity.Property(hash => hash.AHash).HasColumnName("ahash").HasMaxLength(128);
            entity.Property(hash => hash.HashVersion)
                .HasColumnName("hash_version")
                .HasMaxLength(50)
                .HasDefaultValue("mvp-v1")
                .IsRequired();
            entity.Property(hash => hash.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(hash => hash.Video)
                .WithMany(video => video.FrameHashes)
                .HasForeignKey(hash => hash.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(hash => hash.VideoFrame)
                .WithMany(frame => frame.FrameHashes)
                .HasForeignKey(hash => hash.FrameId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(hash => hash.VideoId).HasDatabaseName("ix_frame_hashes_video_id");
            entity.HasIndex(hash => hash.PHash).HasDatabaseName("ix_frame_hashes_phash");
            entity.HasIndex(hash => hash.DHash).HasDatabaseName("ix_frame_hashes_dhash");
            entity.HasIndex(hash => hash.AHash).HasDatabaseName("ix_frame_hashes_ahash");
            entity.HasIndex(hash => new { hash.FrameId, hash.HashVersion })
                .IsUnique()
                .HasDatabaseName("ux_frame_hashes_frame_id_hash_version");
        });
    }

    private static void ConfigureSourceMatch(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SourceMatch>(entity =>
        {
            entity.ToTable("source_matches", table =>
            {
                table.HasCheckConstraint("ck_source_matches_similarity_score", "similarity_score >= 0 AND similarity_score <= 1");
                table.HasCheckConstraint("ck_source_matches_duration_match_score", "duration_match_score IS NULL OR (duration_match_score >= 0 AND duration_match_score <= 1)");
                table.HasCheckConstraint("ck_source_matches_hash_match_score", "hash_match_score IS NULL OR (hash_match_score >= 0 AND hash_match_score <= 1)");
                table.HasCheckConstraint("ck_source_matches_metadata_match_score", "metadata_match_score IS NULL OR (metadata_match_score >= 0 AND metadata_match_score <= 1)");
                table.HasCheckConstraint("ck_source_matches_source_credibility_score", "source_credibility_score IS NULL OR (source_credibility_score >= 0 AND source_credibility_score <= 1)");
            });
            entity.HasKey(match => match.Id);

            entity.Property(match => match.Id).HasColumnName("id").UseIdentityByDefaultColumn();
            entity.Property(match => match.VideoId).HasColumnName("video_id").IsRequired();
            entity.Property(match => match.Platform).HasColumnName("platform").HasMaxLength(100).IsRequired();
            entity.Property(match => match.Url).HasColumnName("url");
            entity.Property(match => match.Title).HasColumnName("title");
            entity.Property(match => match.UploaderName).HasColumnName("uploader_name");
            entity.Property(match => match.UploadDatetime).HasColumnName("upload_datetime");
            entity.Property(match => match.SimilarityScore).HasColumnName("similarity_score").IsRequired();
            entity.Property(match => match.DurationMatchScore).HasColumnName("duration_match_score");
            entity.Property(match => match.HashMatchScore).HasColumnName("hash_match_score");
            entity.Property(match => match.MetadataMatchScore).HasColumnName("metadata_match_score");
            entity.Property(match => match.SourceCredibilityScore).HasColumnName("source_credibility_score");
            entity.Property(match => match.Rank).HasColumnName("rank").HasDefaultValue(1).IsRequired();
            entity.Property(match => match.Confidence)
                .HasColumnName("confidence")
                .HasColumnType("confidence_level")
                .HasSentinel((ConfidenceLevel)(-1))
                .HasDefaultValueSql("'Medium'::confidence_level")
                .IsRequired();
            entity.Property(match => match.DetailsJson)
                .HasColumnName("details_json")
                .HasColumnType("jsonb");
            entity.Property(match => match.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()")
                .IsRequired();

            entity.HasOne(match => match.Video)
                .WithMany(video => video.SourceMatches)
                .HasForeignKey(match => match.VideoId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(match => new { match.VideoId, match.Rank })
                .HasDatabaseName("ix_source_matches_video_rank");
            entity.HasIndex(match => match.UploadDatetime)
                .HasDatabaseName("ix_source_matches_upload_datetime");
        });
    }
}
