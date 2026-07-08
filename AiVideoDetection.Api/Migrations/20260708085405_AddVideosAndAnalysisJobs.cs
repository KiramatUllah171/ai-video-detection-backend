using System;
using AiVideoDetection.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVideosAndAnalysisJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .Annotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin");

            migrationBuilder.CreateTable(
                name: "videos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    original_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    thumbnail_url = table.Column<string>(type: "text", nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_extension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    duration_seconds = table.Column<decimal>(type: "numeric", nullable: true),
                    format_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sha256_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<VideoStatus>(type: "video_status", nullable: false, defaultValueSql: "'Uploaded'::video_status"),
                    retention_delete_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_videos", x => x.id);
                    table.ForeignKey(
                        name: "FK_videos_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "analysis_jobs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<JobStatus>(type: "job_status", nullable: false, defaultValueSql: "'Queued'::job_status"),
                    progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    current_step = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_jobs", x => x.id);
                    table.CheckConstraint("ck_analysis_jobs_progress", "progress >= 0 AND progress <= 100");
                    table.ForeignKey(
                        name: "FK_analysis_jobs_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_status",
                table: "analysis_jobs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_video_id",
                table: "analysis_jobs",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ix_videos_sha256_hash",
                table: "videos",
                column: "sha256_hash");

            migrationBuilder.CreateIndex(
                name: "ix_videos_status",
                table: "videos",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_videos_user_created",
                table: "videos",
                columns: new[] { "user_id", "created_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_jobs");

            migrationBuilder.DropTable(
                name: "videos");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted");
        }
    }
}
