using System;
using AiVideoDetection.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSmartScanSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_analysis_jobs_video_id",
                table: "analysis_jobs");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .Annotation("Npgsql:Enum:analysis_segment_status", "Pending,Preparing,Ready,Analyzing,Completed,Failed,CancelRequested,Cancelled")
                .Annotation("Npgsql:Enum:confidence_level", "Low,Medium,High,Inconclusive")
                .Annotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .Annotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .Annotation("Npgsql:Enum:job_status", "Queued,Preparing,Processing,Finalizing,Completed,Failed,Retrying,CancelRequested,Cancelled")
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .Annotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Cancelled,Deleted")
                .OldAnnotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .OldAnnotation("Npgsql:Enum:confidence_level", "Low,Medium,High,Inconclusive")
                .OldAnnotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .OldAnnotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .OldAnnotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted");

            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS analyzed_coverage_seconds numeric;");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS cancel_requested boolean NOT NULL DEFAULT false;");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS cancel_requested_at timestamp with time zone;");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS completed_segments integer NOT NULL DEFAULT 0;");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS failed_at timestamp with time zone;");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS failed_stage character varying(100);");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS last_activity_at timestamp with time zone;");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS scan_mode character varying(50);");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS total_duration_seconds numeric;");
            migrationBuilder.Sql("ALTER TABLE analysis_jobs ADD COLUMN IF NOT EXISTS total_segments integer NOT NULL DEFAULT 0;");

            migrationBuilder.CreateTable(
                name: "analysis_segments",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    analysis_job_id = table.Column<long>(type: "bigint", nullable: false),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    segment_index = table.Column<int>(type: "integer", nullable: false),
                    start_time = table.Column<decimal>(type: "numeric", nullable: false),
                    end_time = table.Column<decimal>(type: "numeric", nullable: false),
                    duration = table.Column<decimal>(type: "numeric", nullable: false),
                    local_temporary_path = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<AnalysisSegmentStatus>(type: "analysis_segment_status", nullable: false, defaultValueSql: "'Pending'::analysis_segment_status"),
                    progress = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    provider_request_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ai_score = table.Column<decimal>(type: "numeric", nullable: true),
                    confidence = table.Column<decimal>(type: "numeric", nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: true),
                    error_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    safe_error_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analysis_segments", x => x.id);
                    table.CheckConstraint("ck_analysis_segments_progress", "progress >= 0 AND progress <= 100");
                    table.CheckConstraint("ck_analysis_segments_times", "end_time > start_time AND duration > 0");
                    table.ForeignKey(
                        name: "FK_analysis_segments_analysis_jobs_analysis_job_id",
                        column: x => x.analysis_job_id,
                        principalTable: "analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_analysis_segments_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IF NOT EXISTS ux_analysis_jobs_one_active_per_video ON analysis_jobs (video_id) WHERE status NOT IN ('Completed', 'Failed', 'Cancelled');");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_segments_status",
                table: "analysis_segments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_analysis_segments_video_id",
                table: "analysis_segments",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ux_analysis_segments_job_index",
                table: "analysis_segments",
                columns: new[] { "analysis_job_id", "segment_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_segments");

            migrationBuilder.DropIndex(
                name: "ux_analysis_jobs_one_active_per_video",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "analyzed_coverage_seconds",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "cancel_requested",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "cancel_requested_at",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "completed_segments",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "failed_at",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "failed_stage",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "scan_mode",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "total_duration_seconds",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "total_segments",
                table: "analysis_jobs");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .Annotation("Npgsql:Enum:confidence_level", "Low,Medium,High,Inconclusive")
                .Annotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .Annotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .Annotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .Annotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted")
                .OldAnnotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .OldAnnotation("Npgsql:Enum:analysis_segment_status", "Pending,Preparing,Ready,Analyzing,Completed,Failed,CancelRequested,Cancelled")
                .OldAnnotation("Npgsql:Enum:confidence_level", "Low,Medium,High,Inconclusive")
                .OldAnnotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .OldAnnotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .OldAnnotation("Npgsql:Enum:job_status", "Queued,Preparing,Processing,Finalizing,Completed,Failed,Retrying,CancelRequested,Cancelled")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Cancelled,Deleted");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_video_id",
                table: "analysis_jobs",
                column: "video_id");
        }
    }
}
