using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiVideoDetection.Api.Migrations
{
    /// <inheritdoc />
    public partial class ReduceMaxVideoUploadSizeTo300Mb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: 2L,
                column: "max_video_size_bytes",
                value: 262144000L);

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: 3L,
                column: "max_video_size_bytes",
                value: 314572800L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: 2L,
                column: "max_video_size_bytes",
                value: 314572800L);

            migrationBuilder.UpdateData(
                table: "subscription_plans",
                keyColumn: "id",
                keyValue: 3L,
                column: "max_video_size_bytes",
                value: 524288000L);
        }
    }
}
