using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRetentionCleanupRunMetrics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "retention_cleanup_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: false),
                    work_directories_deleted = table.Column<int>(type: "integer", nullable: false),
                    frame_objects_cleared = table.Column<int>(type: "integer", nullable: false),
                    original_videos_cleared = table.Column<int>(type: "integer", nullable: false),
                    thumbnails_cleared = table.Column<int>(type: "integer", nullable: false),
                    evidence_rows_deleted = table.Column<int>(type: "integer", nullable: false),
                    source_match_rows_deleted = table.Column<int>(type: "integer", nullable: false),
                    provider_payloads_cleared = table.Column<int>(type: "integer", nullable: false),
                    analysis_payloads_cleared = table.Column<int>(type: "integer", nullable: false),
                    segment_payloads_cleared = table.Column<int>(type: "integer", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_cleanup_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_retention_cleanup_runs_job_started",
                table: "retention_cleanup_runs",
                columns: new[] { "job_name", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_retention_cleanup_runs_started",
                table: "retention_cleanup_runs",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_retention_cleanup_runs_status_started",
                table: "retention_cleanup_runs",
                columns: new[] { "status", "started_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "retention_cleanup_runs");
        }
    }
}
