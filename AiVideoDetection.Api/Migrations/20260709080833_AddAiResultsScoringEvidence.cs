using System;
using AiVideoDetection.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAiResultsScoringEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .Annotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .Annotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .Annotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .Annotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted")
                .OldAnnotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted");

            migrationBuilder.CreateTable(
                name: "model_versions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_model_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ai_results",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    model_version_id = table.Column<long>(type: "bigint", nullable: true),
                    visual_score = table.Column<decimal>(type: "numeric", nullable: false),
                    temporal_score = table.Column<decimal>(type: "numeric", nullable: true),
                    metadata_score = table.Column<decimal>(type: "numeric", nullable: true),
                    final_score = table.Column<decimal>(type: "numeric", nullable: false),
                    confidence = table.Column<decimal>(type: "numeric", nullable: false),
                    label = table.Column<AnalysisLabel>(type: "analysis_label", nullable: false),
                    raw_model_output_json = table.Column<string>(type: "jsonb", nullable: false),
                    summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_results", x => x.id);
                    table.ForeignKey(
                        name: "FK_ai_results_model_versions_model_version_id",
                        column: x => x.model_version_id,
                        principalTable: "model_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ai_results_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evidence_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ai_result_id = table.Column<long>(type: "bigint", nullable: false),
                    video_frame_id = table.Column<long>(type: "bigint", nullable: true),
                    type = table.Column<EvidenceType>(type: "evidence_type", nullable: false),
                    severity = table.Column<EvidenceSeverity>(type: "evidence_severity", nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    score_impact = table.Column<decimal>(type: "numeric", nullable: true),
                    timestamp_seconds = table.Column<decimal>(type: "numeric", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_evidence_items_ai_results_ai_result_id",
                        column: x => x.ai_result_id,
                        principalTable: "ai_results",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_evidence_items_video_frames_video_frame_id",
                        column: x => x.video_frame_id,
                        principalTable: "video_frames",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "model_versions",
                columns: new[] { "id", "created_at", "description", "is_active", "name", "version" },
                values: new object[] { 1L, new DateTimeOffset(new DateTime(2026, 7, 9, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "Mock deterministic AI scoring model for pipeline integration.", true, "Mock Video AI", "mock-video-ai-v1" });

            migrationBuilder.CreateIndex(
                name: "ix_ai_results_label",
                table: "ai_results",
                column: "label");

            migrationBuilder.CreateIndex(
                name: "ix_ai_results_model_version_id",
                table: "ai_results",
                column: "model_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_ai_results_video_id_created",
                table: "ai_results",
                columns: new[] { "video_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_evidence_items_ai_result_id",
                table: "evidence_items",
                column: "ai_result_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_items_severity",
                table: "evidence_items",
                column: "severity");

            migrationBuilder.CreateIndex(
                name: "ix_evidence_items_video_frame_id",
                table: "evidence_items",
                column: "video_frame_id");

            migrationBuilder.CreateIndex(
                name: "ix_model_versions_active",
                table: "model_versions",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ux_model_versions_version",
                table: "model_versions",
                column: "version",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidence_items");

            migrationBuilder.DropTable(
                name: "ai_results");

            migrationBuilder.DropTable(
                name: "model_versions");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .Annotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted")
                .OldAnnotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .OldAnnotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .OldAnnotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .OldAnnotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted");
        }
    }
}
