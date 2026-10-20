using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionReceiptAndRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Module 6. All additive except the unique index on Answers(SessionId, QuestionId), which replaces
            // the plain SessionId index (the composite one serves the same lookups). save-answer has always upserted
            // one row per (session, question), but a double-click race could have stored two. Check first:
            //   SELECT "SessionId", "QuestionId", count(*) FROM "Answers" GROUP BY 1, 2 HAVING count(*) > 1;
            // and resolve any duplicates by hand (keep the latest LastSavedAt; its grading rows cascade with it).
            // Existing answers get Revision 0 (unrevisioned) and existing sessions no receipt / no extra time.
            migrationBuilder.DropIndex(
                name: "IX_Answers_SessionId",
                table: "Answers");

            migrationBuilder.AddColumn<int>(
                name: "ExtraMinutes",
                table: "ExamSessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReceiptAnswerCount",
                table: "ExamSessions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptSha256",
                table: "ExamSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClientSavedAt",
                table: "Answers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Revision",
                table: "Answers",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Answers_SessionId_QuestionId",
                table: "Answers",
                columns: new[] { "SessionId", "QuestionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Answers_SessionId_QuestionId",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "ExtraMinutes",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ReceiptAnswerCount",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ReceiptSha256",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ClientSavedAt",
                table: "Answers");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "Answers");

            migrationBuilder.CreateIndex(
                name: "IX_Answers_SessionId",
                table: "Answers",
                column: "SessionId");
        }
    }
}
