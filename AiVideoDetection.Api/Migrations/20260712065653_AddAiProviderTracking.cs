using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiProviderTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "external_completed_at",
                table: "ai_results",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "external_confidence",
                table: "ai_results",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_error_message",
                table: "ai_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_label",
                table: "ai_results",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_provider_job_id",
                table: "ai_results",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_provider_name",
                table: "ai_results",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_provider_result_id",
                table: "ai_results",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_provider_status",
                table: "ai_results",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_raw_response_json",
                table: "ai_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "external_requested_at",
                table: "ai_results",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "external_score",
                table: "ai_results",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fallback_reason",
                table: "ai_results",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "fallback_used",
                table: "ai_results",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "final_decision_source",
                table: "ai_results",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.AddColumn<string>(
                name: "hybrid_result_json",
                table: "ai_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "local_result_json",
                table: "ai_results",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "ai_results",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Local");

            migrationBuilder.AddColumn<string>(
                name: "provider_mode",
                table: "ai_results",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "local");

            migrationBuilder.CreateTable(
                name: "ai_provider_requests",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    analysis_job_id = table.Column<long>(type: "bigint", nullable: false),
                    ai_result_id = table.Column<long>(type: "bigint", nullable: true),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    provider_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_mode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    provider_request_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    provider_job_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    request_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    raw_request_metadata_json = table.Column<string>(type: "jsonb", nullable: true),
                    raw_response_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_provider_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_provider_requests_ai_results_ai_result_id",
                        column: x => x.ai_result_id,
                        principalTable: "ai_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ai_provider_requests_analysis_jobs_analysis_job_id",
                        column: x => x.analysis_job_id,
                        principalTable: "analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_provider_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ai_provider_requests_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "api_usage_monthly",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    provider_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    year = table.Column<int>(type: "integer", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: false),
                    request_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    success_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    failed_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    quota_limit = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_usage_monthly", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ai_results_provider",
                table: "ai_results",
                column: "provider");

            migrationBuilder.CreateIndex(
                name: "IX_ai_provider_requests_ai_result_id",
                table: "ai_provider_requests",
                column: "ai_result_id");

            migrationBuilder.CreateIndex(
                name: "IX_ai_provider_requests_analysis_job_id",
                table: "ai_provider_requests",
                column: "analysis_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_requests_provider_job",
                table: "ai_provider_requests",
                columns: new[] { "provider_name", "provider_job_id" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_provider_requests_user_id",
                table: "ai_provider_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_provider_requests_video_created",
                table: "ai_provider_requests",
                columns: new[] { "video_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_api_usage_monthly_provider_year_month",
                table: "api_usage_monthly",
                columns: new[] { "provider_name", "year", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_provider_requests");

            migrationBuilder.DropTable(
                name: "api_usage_monthly");

            migrationBuilder.DropIndex(
                name: "ix_ai_results_provider",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_completed_at",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_confidence",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_error_message",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_label",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_provider_job_id",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_provider_name",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_provider_result_id",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_provider_status",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_raw_response_json",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_requested_at",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "external_score",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "fallback_reason",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "fallback_used",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "final_decision_source",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "hybrid_result_json",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "local_result_json",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "ai_results");

            migrationBuilder.DropColumn(
                name: "provider_mode",
                table: "ai_results");
        }
    }
}
