using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptureIntervals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScreenshotIntervalSeconds",
                table: "Exams",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<int>(
                name: "WebcamIntervalSeconds",
                table: "Exams",
                type: "integer",
                nullable: false,
                defaultValue: 60);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScreenshotIntervalSeconds",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "WebcamIntervalSeconds",
                table: "Exams");
        }
    }
}
