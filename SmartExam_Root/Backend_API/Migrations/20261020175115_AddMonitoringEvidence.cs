using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MonitoringEvents_ExamSessionId",
                table: "MonitoringEvents");

            migrationBuilder.AddColumn<Guid>(
                name: "ClientEventId",
                table: "MonitoringEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceivedAt",
                table: "MonitoringEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisconnectedAt",
                table: "ExamSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDisconnected",
                table: "ExamSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastHeartbeatAt",
                table: "ExamSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Screenshots",
                columns: table => new
                {
                    ScreenshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TriggerReason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<int>(type: "integer", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screenshots", x => x.ScreenshotId);
                    table.ForeignKey(
                        name: "FK_Screenshots_ExamSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ExamSessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_ExamSessionId_ClientEventId",
                table: "MonitoringEvents",
                columns: new[] { "ExamSessionId", "ClientEventId" },
                unique: true,
                filter: "\"ClientEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_ExamSessionId_RecordedAt",
                table: "MonitoringEvents",
                columns: new[] { "ExamSessionId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExamSessions_Status_LastHeartbeatAt",
                table: "ExamSessions",
                columns: new[] { "Status", "LastHeartbeatAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Screenshots_CapturedAt",
                table: "Screenshots",
                column: "CapturedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Screenshots_ExamId",
                table: "Screenshots",
                column: "ExamId");

            migrationBuilder.CreateIndex(
                name: "IX_Screenshots_SessionId_CapturedAt",
                table: "Screenshots",
                columns: new[] { "SessionId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Screenshots");

            migrationBuilder.DropIndex(
                name: "IX_MonitoringEvents_ExamSessionId_ClientEventId",
                table: "MonitoringEvents");

            migrationBuilder.DropIndex(
                name: "IX_MonitoringEvents_ExamSessionId_RecordedAt",
                table: "MonitoringEvents");

            migrationBuilder.DropIndex(
                name: "IX_ExamSessions_Status_LastHeartbeatAt",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "ClientEventId",
                table: "MonitoringEvents");

            migrationBuilder.DropColumn(
                name: "ReceivedAt",
                table: "MonitoringEvents");

            migrationBuilder.DropColumn(
                name: "DisconnectedAt",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "IsDisconnected",
                table: "ExamSessions");

            migrationBuilder.DropColumn(
                name: "LastHeartbeatAt",
                table: "ExamSessions");

            migrationBuilder.CreateIndex(
                name: "IX_MonitoringEvents_ExamSessionId",
                table: "MonitoringEvents",
                column: "ExamSessionId");
        }
    }
}
