using System;
using AiVideoDetection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260731164500_AddAnalysisPauseResume")]
    public partial class AddAnalysisPauseResume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TYPE job_status ADD VALUE IF NOT EXISTS 'PauseRequested';", suppressTransaction: true);
            migrationBuilder.Sql("ALTER TYPE job_status ADD VALUE IF NOT EXISTS 'Paused';", suppressTransaction: true);
            migrationBuilder.Sql("ALTER TYPE job_status ADD VALUE IF NOT EXISTS 'ResumeRequested';", suppressTransaction: true);

            migrationBuilder.AddColumn<string>(
                name: "last_checkpoint",
                table: "analysis_jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "pause_requested_at",
                table: "analysis_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "pause_requested",
                table: "analysis_jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "paused_at",
                table: "analysis_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paused_from_stage",
                table: "analysis_jobs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resume_background_job_id",
                table: "analysis_jobs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "resumed_at",
                table: "analysis_jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_checkpoint",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "pause_requested",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "pause_requested_at",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "paused_at",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "paused_from_stage",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "resume_background_job_id",
                table: "analysis_jobs");

            migrationBuilder.DropColumn(
                name: "resumed_at",
                table: "analysis_jobs");
        }
    }
}
