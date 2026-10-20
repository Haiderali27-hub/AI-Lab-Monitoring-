using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAcademicsAndExamProctors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Module 7.
            // 1. Course -> Department used a shadow FK column (DepartmentDeptId) while the mapped DeptId stayed
            //    all-zero for rows created through the navigation (the demo seed). Copy the real department into
            //    DeptId before the shadow column is dropped, so no course loses its department.
            // 2. The two new unique indexes fail on a database that already holds duplicates. Check first:
            //      SELECT "Name", count(*) FROM "Departments" GROUP BY "Name" HAVING count(*) > 1;
            //      SELECT "DepartmentDeptId", "Code", count(*) FROM "Courses" GROUP BY 1, 2 HAVING count(*) > 1;
            migrationBuilder.Sql(
                "UPDATE \"Courses\" SET \"DeptId\" = \"DepartmentDeptId\" WHERE \"DeptId\" <> \"DepartmentDeptId\";");

            migrationBuilder.DropForeignKey(
                name: "FK_Courses_Departments_DepartmentDeptId",
                table: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_Courses_DepartmentDeptId",
                table: "Courses");

            migrationBuilder.DropColumn(
                name: "DepartmentDeptId",
                table: "Courses");

            migrationBuilder.CreateTable(
                name: "ExamProctors",
                columns: table => new
                {
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExamProctors", x => new { x.ExamId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ExamProctors_Exams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "Exams",
                        principalColumn: "ExamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExamProctors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_DeptId_Code",
                table: "Courses",
                columns: new[] { "DeptId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExamProctors_UserId",
                table: "ExamProctors",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_Departments_DeptId",
                table: "Courses",
                column: "DeptId",
                principalTable: "Departments",
                principalColumn: "DeptId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Courses_Departments_DeptId",
                table: "Courses");

            migrationBuilder.DropTable(
                name: "ExamProctors");

            migrationBuilder.DropIndex(
                name: "IX_Departments_Name",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Courses_DeptId_Code",
                table: "Courses");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentDeptId",
                table: "Courses",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.Sql("UPDATE \"Courses\" SET \"DepartmentDeptId\" = \"DeptId\";");

            migrationBuilder.CreateIndex(
                name: "IX_Courses_DepartmentDeptId",
                table: "Courses",
                column: "DepartmentDeptId");

            migrationBuilder.AddForeignKey(
                name: "FK_Courses_Departments_DepartmentDeptId",
                table: "Courses",
                column: "DepartmentDeptId",
                principalTable: "Departments",
                principalColumn: "DeptId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
