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

        ConfigureUser(modelBuilder);
        ConfigureRefreshToken(modelBuilder);
        ConfigureVideo(modelBuilder);
        ConfigureAnalysisJob(modelBuilder);
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
}
