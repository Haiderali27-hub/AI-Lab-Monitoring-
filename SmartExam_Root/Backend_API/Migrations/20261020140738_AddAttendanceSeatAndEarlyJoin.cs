using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceSeatAndEarlyJoin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HostName",
                table: "Workstations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EarlyJoinMinutes",
                table: "Exams",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing exams keep working exactly as before: seat enforcement is opt-in for rows that
            // predate this migration (new exams default to true from the model / CreateExamDto).
            migrationBuilder.AddColumn<bool>(
                name: "EnforceSeat",
                table: "Exams",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "MinAttendancePercent",
                table: "Exams",
                type: "double precision",
                nullable: false,
                defaultValue: 75.0);   // institutional default threshold (thesis FR9)

            migrationBuilder.AddColumn<bool>(
                name: "EligibilityOverridden",
                table: "ExamAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EligibilitySource",
                table: "ExamAssignments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceRecords",
                columns: table => new
                {
                    AttendanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TotalClasses = table.Column<int>(type: "integer", nullable: false),
                    AttendedClasses = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImportedBy = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceRecords", x => x.AttendanceId);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "SectionId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AttendanceRecords_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_SectionId_UserId",
                table: "AttendanceRecords",
                columns: new[] { "SectionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceRecords_UserId",
                table: "AttendanceRecords",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AttendanceRecords");

            migrationBuilder.DropColumn(
                name: "HostName",
                table: "Workstations");

            migrationBuilder.DropColumn(
                name: "EarlyJoinMinutes",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "EnforceSeat",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "MinAttendancePercent",
                table: "Exams");

            migrationBuilder.DropColumn(
                name: "EligibilityOverridden",
                table: "ExamAssignments");

            migrationBuilder.DropColumn(
                name: "EligibilitySource",
                table: "ExamAssignments");
        }
    }
}
