using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class FixAnswerSessionFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing Answer rows were written with the disconnected SessionId column
            // (left at Guid.Empty by the old shadow-FK mapping). They would violate the
            // new FK below, so clear them first — answer rows are transient seed data
            // (the seeder repopulates on startup).
            migrationBuilder.Sql("DELETE FROM \"Answers\";");

            migrationBuilder.DropForeignKey(
                name: "FK_Answers_ExamSessions_ExamSessionSessionId",
                table: "Answers");

            migrationBuilder.DropIndex(
                name: "IX_Answers_ExamSessionSessionId",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "ExamSessionSessionId",
                table: "Answers");

            migrationBuilder.CreateIndex(
                name: "IX_Answers_SessionId",
                table: "Answers",
                column: "SessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Answers_ExamSessions_SessionId",
                table: "Answers",
                column: "SessionId",
                principalTable: "ExamSessions",
                principalColumn: "SessionId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Answers_ExamSessions_SessionId",
                table: "Answers");

            migrationBuilder.DropIndex(
                name: "IX_Answers_SessionId",
                table: "Answers");

            migrationBuilder.AddColumn<Guid>(
                name: "ExamSessionSessionId",
                table: "Answers",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Answers_ExamSessionSessionId",
                table: "Answers",
                column: "ExamSessionSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Answers_ExamSessions_ExamSessionSessionId",
                table: "Answers",
                column: "ExamSessionSessionId",
                principalTable: "ExamSessions",
                principalColumn: "SessionId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
