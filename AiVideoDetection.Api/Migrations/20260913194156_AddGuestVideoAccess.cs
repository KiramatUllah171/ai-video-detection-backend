using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGuestVideoAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "guest_access_expires_at",
                table: "videos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "guest_access_token_hash",
                table: "videos",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "guest_claimed_at",
                table: "videos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_videos_guest_access_token_hash",
                table: "videos",
                column: "guest_access_token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_videos_guest_access_token_hash",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "guest_access_expires_at",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "guest_access_token_hash",
                table: "videos");

            migrationBuilder.DropColumn(
                name: "guest_claimed_at",
                table: "videos");
        }
    }
}
