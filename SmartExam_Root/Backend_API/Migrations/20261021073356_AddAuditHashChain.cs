using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend_API.Migrations
{
    /// <summary>
    /// Module 12 — tamper-evident audit log. Adds AuditLogs.Sequence (unique) / PrevHash / Hash and drops the
    /// ActorId → Users foreign key (the hash covers ActorId, so a user deletion must no longer null it; the column and
    /// its index stay). Purely additive for the data: existing rows keep null chain columns ("unsealed") and new rows are
    /// chained from sequence 1 as they are written. After applying, an admin seals the old rows once with
    /// POST /api/audit-logs/backfill (oldest first, appended to the chain, audited AUDIT_CHAIN_BACKFILLED); until then
    /// GET /api/audit-logs/verify reports valid = false with the unsealed count. Hashes are computed in the application
    /// (canonical JSON, microsecond timestamps), not in SQL.
    /// </summary>
    public partial class AddAuditHashChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Users_ActorId",
                table: "AuditLogs");

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrevHash",
                table: "AuditLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "AuditLogs",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs",
                column: "Sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Sequence",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "PrevHash",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "AuditLogs");

            // Rows of users deleted while there was no foreign key would block it: clear those ids first (the old behaviour).
            migrationBuilder.Sql(
                "UPDATE \"AuditLogs\" SET \"ActorId\" = NULL " +
                "WHERE \"ActorId\" IS NOT NULL AND NOT EXISTS (SELECT 1 FROM \"Users\" u WHERE u.\"UserId\" = \"AuditLogs\".\"ActorId\");");

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Users_ActorId",
                table: "AuditLogs",
                column: "ActorId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
