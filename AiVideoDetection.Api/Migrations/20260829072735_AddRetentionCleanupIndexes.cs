using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionCleanupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_requests_created_at",
                table: "ai_provider_requests",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_ai_results_created_at",
                table: "ai_results",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_analysis_segments_created_at",
                table: "analysis_segments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_items_created_at",
                table: "evidence_items",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_source_matches_created_at",
                table: "source_matches",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_video_frames_created_at",
                table: "video_frames",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_videos_retention_delete_at",
                table: "videos",
                column: "retention_delete_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ai_provider_requests_created_at",
                table: "ai_provider_requests");

            migrationBuilder.DropIndex(
                name: "ix_ai_results_created_at",
                table: "ai_results");

            migrationBuilder.DropIndex(
                name: "ix_analysis_segments_created_at",
                table: "analysis_segments");

            migrationBuilder.DropIndex(
                name: "ix_evidence_items_created_at",
                table: "evidence_items");

            migrationBuilder.DropIndex(
                name: "ix_source_matches_created_at",
                table: "source_matches");

            migrationBuilder.DropIndex(
                name: "ix_video_frames_created_at",
                table: "video_frames");

            migrationBuilder.DropIndex(
                name: "ix_videos_retention_delete_at",
                table: "videos");
        }
    }
}
