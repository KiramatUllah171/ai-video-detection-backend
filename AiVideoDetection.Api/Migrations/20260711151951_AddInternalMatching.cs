using System;
using AiVideoDetection.Domain.Enums;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddInternalMatching : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .Annotation("Npgsql:Enum:confidence_level", "Low,Medium,High,Inconclusive")
                .Annotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .Annotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .Annotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .Annotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted")
                .OldAnnotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .OldAnnotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .OldAnnotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .OldAnnotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted");

            migrationBuilder.CreateTable(
                name: "frame_hashes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    frame_id = table.Column<long>(type: "bigint", nullable: false),
                    phash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    dhash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ahash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    hash_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "mvp-v1"),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_frame_hashes", x => x.id);
                    table.ForeignKey(
                        name: "FK_frame_hashes_video_frames_frame_id",
                        column: x => x.frame_id,
                        principalTable: "video_frames",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_frame_hashes_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "source_matches",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    video_id = table.Column<long>(type: "bigint", nullable: false),
                    platform = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    url = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    uploader_name = table.Column<string>(type: "text", nullable: true),
                    upload_datetime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    similarity_score = table.Column<decimal>(type: "numeric", nullable: false),
                    duration_match_score = table.Column<decimal>(type: "numeric", nullable: true),
                    hash_match_score = table.Column<decimal>(type: "numeric", nullable: true),
                    metadata_match_score = table.Column<decimal>(type: "numeric", nullable: true),
                    source_credibility_score = table.Column<decimal>(type: "numeric", nullable: true),
                    rank = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    confidence = table.Column<ConfidenceLevel>(type: "confidence_level", nullable: false, defaultValueSql: "'Medium'::confidence_level"),
                    details_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_matches", x => x.id);
                    table.CheckConstraint("ck_source_matches_duration_match_score", "duration_match_score IS NULL OR (duration_match_score >= 0 AND duration_match_score <= 1)");
                    table.CheckConstraint("ck_source_matches_hash_match_score", "hash_match_score IS NULL OR (hash_match_score >= 0 AND hash_match_score <= 1)");
                    table.CheckConstraint("ck_source_matches_metadata_match_score", "metadata_match_score IS NULL OR (metadata_match_score >= 0 AND metadata_match_score <= 1)");
                    table.CheckConstraint("ck_source_matches_similarity_score", "similarity_score >= 0 AND similarity_score <= 1");
                    table.CheckConstraint("ck_source_matches_source_credibility_score", "source_credibility_score IS NULL OR (source_credibility_score >= 0 AND source_credibility_score <= 1)");
                    table.ForeignKey(
                        name: "FK_source_matches_videos_video_id",
                        column: x => x.video_id,
                        principalTable: "videos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_frame_hashes_ahash",
                table: "frame_hashes",
                column: "ahash");

            migrationBuilder.CreateIndex(
                name: "ix_frame_hashes_dhash",
                table: "frame_hashes",
                column: "dhash");

            migrationBuilder.CreateIndex(
                name: "ix_frame_hashes_phash",
                table: "frame_hashes",
                column: "phash");

            migrationBuilder.CreateIndex(
                name: "ix_frame_hashes_video_id",
                table: "frame_hashes",
                column: "video_id");

            migrationBuilder.CreateIndex(
                name: "ux_frame_hashes_frame_id_hash_version",
                table: "frame_hashes",
                columns: new[] { "frame_id", "hash_version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_matches_upload_datetime",
                table: "source_matches",
                column: "upload_datetime");

            migrationBuilder.CreateIndex(
                name: "ix_source_matches_video_rank",
                table: "source_matches",
                columns: new[] { "video_id", "rank" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "frame_hashes");

            migrationBuilder.DropTable(
                name: "source_matches");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .Annotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .Annotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .Annotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .Annotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .Annotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted")
                .OldAnnotation("Npgsql:Enum:analysis_label", "LikelyReal,LikelyAiGenerated,EditedManipulated,Suspicious,Inconclusive")
                .OldAnnotation("Npgsql:Enum:confidence_level", "Low,Medium,High,Inconclusive")
                .OldAnnotation("Npgsql:Enum:evidence_severity", "Low,Medium,High,Critical")
                .OldAnnotation("Npgsql:Enum:evidence_type", "AiFrameScore,MetadataWarning,ProcessingWarning,ConfidenceNote,SystemNote")
                .OldAnnotation("Npgsql:Enum:job_status", "Queued,Processing,Completed,Failed,Retrying,Cancelled")
                .OldAnnotation("Npgsql:Enum:user_role", "User,Admin,Reviewer,EnterpriseAdmin")
                .OldAnnotation("Npgsql:Enum:video_status", "Uploaded,Queued,Processing,Completed,Failed,Deleted");
        }
    }
}
