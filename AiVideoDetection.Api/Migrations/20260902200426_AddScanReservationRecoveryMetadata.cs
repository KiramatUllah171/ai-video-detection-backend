using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddScanReservationRecoveryMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expires_at",
                table: "scan_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_heartbeat_at",
                table: "scan_reservations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_scan_reservations_status_expires",
                table: "scan_reservations",
                columns: new[] { "status", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scan_reservations_status_expires",
                table: "scan_reservations");

            migrationBuilder.DropColumn(
                name: "expires_at",
                table: "scan_reservations");

            migrationBuilder.DropColumn(
                name: "last_heartbeat_at",
                table: "scan_reservations");
        }
    }
}
