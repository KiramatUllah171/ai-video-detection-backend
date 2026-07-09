using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerFfmpegProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "job_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    job_id = table.Column<long>(type: "bigint", nullable: false),
                    step_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    level = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    details_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_job_logs_analysis_jobs_job_id",
                        column: x => x.job_id,
                        principalTable: "analysis_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    codec = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    audio_codec = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    fps = table.Column<decimal>(type: "numeric", nullable: true),
                    resolution = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    duration_seconds = table.Column<decimal>(type: "numeric", nullable: true),
                    bitrate = table.Column<long>(type: "bigint", nullable: true),
                    encoder = table.Column<string>(type: "text", nullable: true),
                    creation_time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    has_missing_metadata = table.Column<bool>(type: "boolean", nullable: false),
                    warnings_json = table.Column<string>(type: "jsonb", nullable: true),
                    raw_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_metadata_results_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "video_frames",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    frame_url = table.Column<string>(type: "text", nullable: false),
                    timestamp_seconds = table.Column<decimal>(type: "numeric", nullable: false),
                    frame_index = table.Column<int>(type: "integer", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    is_keyframe = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_video_frames", x => x.id);
                    table.ForeignKey(
                        name: "FK_video_frames_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_job_logs_job_id_created",
                table: "job_logs",
                columns: new[] { "job_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_metadata_results_video_id",
                table: "metadata_results",
                column: "video_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_video_frames_video_id",
                table: "video_frames",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ux_video_frames_video_id_frame_index",
                table: "video_frames",
                columns: new[] { "video_id", "frame_index" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "job_logs");

            migrationBuilder.DropTable(
                name: "metadata_results");

            migrationBuilder.DropTable(
                name: "video_frames");
        }
    }
}
