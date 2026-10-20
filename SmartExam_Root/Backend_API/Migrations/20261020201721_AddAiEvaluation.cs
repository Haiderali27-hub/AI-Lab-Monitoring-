using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddAiEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "TestCases",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FloatTolerance",
                table: "TestCases",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IgnoreCase",
                table: "TestCases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsEdgeCase",
                table: "TestCases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "OrderIndex",
                table: "TestCases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TimeLimitMs",
                table: "TestCases",
                type: "integer",
                nullable: true);

            // Existing test cases count once each (weight 1), as they always did.
            migrationBuilder.AddColumn<double>(
                name: "Weight",
                table: "TestCases",
                type: "double precision",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Questions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RubricKeywords",
                table: "Questions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnalysisJson",
                table: "AiGradingResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CompileOutput",
                table: "AiGradingResults",
                type: "character varying(4104)",
                maxLength: 4104,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMs",
                table: "AiGradingResults",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Engine",
                table: "AiGradingResults",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LlmSummary",
                table: "AiGradingResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "QueueVersion",
                table: "AiGradingResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Existing suggestions were produced synchronously at submit: they are Graded (never re-queued).
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "AiGradingResults",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Graded");

            migrationBuilder.CreateTable(
                name: "TestCaseResults",
                columns: table => new
                {
                    TestCaseResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrderIndex = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Input = table.Column<string>(type: "text", nullable: false),
                    ExpectedOutput = table.Column<string>(type: "text", nullable: false),
                    IsHidden = table.Column<bool>(type: "boolean", nullable: false),
                    IsEdgeCase = table.Column<bool>(type: "boolean", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Passed = table.Column<bool>(type: "boolean", nullable: false),
                    ActualOutput = table.Column<string>(type: "character varying(4104)", maxLength: 4104, nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    CompareMode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCaseResults", x => x.TestCaseResultId);
                    table.ForeignKey(
                        name: "FK_TestCaseResults_AiGradingResults_ResultId",
                        column: x => x.ResultId,
                        principalTable: "AiGradingResults",
                        principalColumn: "ResultId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiGradingResults_Status",
                table: "AiGradingResults",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_TestCaseResults_ResultId_OrderIndex",
                table: "TestCaseResults",
                columns: new[] { "ResultId", "OrderIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TestCaseResults");

            migrationBuilder.DropIndex(
                name: "IX_AiGradingResults_Status",
                table: "AiGradingResults");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "FloatTolerance",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "IgnoreCase",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "IsEdgeCase",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "OrderIndex",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "TimeLimitMs",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "RubricKeywords",
                table: "Questions");

            migrationBuilder.DropColumn(
                name: "AnalysisJson",
                table: "AiGradingResults");

            migrationBuilder.DropColumn(
                name: "CompileOutput",
                table: "AiGradingResults");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "AiGradingResults");

            migrationBuilder.DropColumn(
                name: "Engine",
                table: "AiGradingResults");

            migrationBuilder.DropColumn(
                name: "LlmSummary",
                table: "AiGradingResults");

            migrationBuilder.DropColumn(
                name: "QueueVersion",
                table: "AiGradingResults");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "AiGradingResults");
        }
    }
}
