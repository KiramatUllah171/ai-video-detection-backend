using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_videos_deleted_created",
                table: "videos",
                columns: new[] { "deleted_at", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_videos_status_deleted_created",
                table: "videos",
                columns: new[] { "status", "deleted_at", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_id",
                table: "audit_logs",
                columns: new[] { "created_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_created_at",
                table: "analysis_jobs",
                column: "created_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_analysis_jobs_status_created",
                table: "analysis_jobs",
                columns: new[] { "status", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_requests_completed_at",
                table: "ai_provider_requests",
                column: "request_completed_at");

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_requests_started_at",
                table: "ai_provider_requests",
                column: "request_started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_requests_status_started",
                table: "ai_provider_requests",
                columns: new[] { "status", "request_started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_requests_user_started",
                table: "ai_provider_requests",
                columns: new[] { "user_id", "request_started_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_videos_deleted_created",
                table: "videos");

            migrationBuilder.DropIndex(
                name: "ix_videos_status_deleted_created",
                table: "videos");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_created_id",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_analysis_jobs_created_at",
                table: "analysis_jobs");

            migrationBuilder.DropIndex(
                name: "ix_analysis_jobs_status_created",
                table: "analysis_jobs");

            migrationBuilder.DropIndex(
                name: "ix_ai_provider_requests_completed_at",
                table: "ai_provider_requests");

            migrationBuilder.DropIndex(
                name: "ix_ai_provider_requests_started_at",
                table: "ai_provider_requests");

            migrationBuilder.DropIndex(
                name: "ix_ai_provider_requests_status_started",
                table: "ai_provider_requests");

            migrationBuilder.DropIndex(
                name: "ix_ai_provider_requests_user_started",
                table: "ai_provider_requests");
        }
    }
}
