using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCorrelationIdsToLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "job_logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation_id",
                table: "audit_logs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_logs_correlation_id",
                table: "job_logs",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_correlation_id",
                table: "audit_logs",
                column: "correlation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_job_logs_correlation_id",
                table: "job_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_correlation_id",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "job_logs");

            migrationBuilder.DropColumn(
                name: "correlation_id",
                table: "audit_logs");
        }
    }
}
