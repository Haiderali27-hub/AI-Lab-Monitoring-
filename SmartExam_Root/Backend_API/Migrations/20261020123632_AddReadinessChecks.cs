using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddReadinessChecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMock",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireMic",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireReadiness",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequireWebcam",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ReadinessChecks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassedAll = table.Column<bool>(type: "boolean", nullable: false),
                    ResultsJson = table.Column<string>(type: "text", nullable: false),
                    MachineName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadinessChecks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadinessChecks_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReadinessChecks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessChecks_ExamId_UserId_CheckedAt",
                table: "ReadinessChecks",
                columns: new[] { "ExamId", "UserId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReadinessChecks_UserId",
                table: "ReadinessChecks",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReadinessChecks");

            migrationBuilder.DropColumn(
                name: "IsMock",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "RequireMic",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "RequireReadiness",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "RequireWebcam",
                table: "Exams");
        }
    }
}
