using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddLockdownConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BlockMultiMonitor",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "LockdownMode",
                table: "Exams",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Report");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockMultiMonitor",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "LockdownMode",
                table: "Exams");
        }
    }
}
