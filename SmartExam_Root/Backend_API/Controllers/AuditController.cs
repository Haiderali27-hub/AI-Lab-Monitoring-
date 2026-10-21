using System.Security.Claims;
using Backend_API.Data;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;
    public AuditController(AppDbContext db) => _db = db;

    // GET /api/audit-logs?eventType=&take=200  — newest first
    [HttpGet]
    public async Task<IActionResult> GetLogs([FromQuery] string? eventType, [FromQuery] int take = 200)
    {
        take = Math.Clamp(take, 1, 1000);

        var query = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(a => a.EventType == eventType);

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .AsNoTracking()
            .ToListAsync();

        // ActorId is a plain id (Module 12), so a deleted actor's rows keep it: look up the users that still exist.
        var actorIds = rows.Where(a => a.ActorId != null).Select(a => a.ActorId!.Value).Distinct().ToList();
        var actors = await _db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.UserId))
            .Select(u => new { u.UserId, u.Name, u.Email })
            .ToDictionaryAsync(u => u.UserId);

        var logs = rows.Select(a =>
        {
            var actor = a.ActorId is Guid actorId && actors.TryGetValue(actorId, out var found) ? found : null;
            return new
            {
                a.LogId,
                a.EventType,
                a.EntityType,
                a.EntityId,
                a.Details,
                a.CreatedAt,
                ActorName = actor?.Name ?? (a.ActorId is null ? "System" : "Deleted user"),
                ActorEmail = actor?.Email,
                a.ActorId,
                a.Sequence,
                a.Hash
            };
        }).ToList();

        return Ok(logs);
    }

    // GET /api/audit-logs/verify — Module 12: walks the hash chain; { valid, brokenAt, reason, checkedCount, unsealed, … }
    [HttpGet("verify")]
    public async Task<IActionResult> Verify(CancellationToken cancellationToken)
    {
        var status = await AuditChain.VerifyAsync(_db, cancellationToken);
        return Ok(new
        {
            status.Valid,
            status.BrokenAt,
            status.Reason,
            status.CheckedCount,
            status.Unsealed,
            status.LastVerifiedSequence,
            status.LastVerifiedHash,
            verifiedAtUtc = DateTime.UtcNow
        });
    }

    // POST /api/audit-logs/backfill — Module 12: seals the rows written before the AddAuditHashChain migration
    // (oldest first, appended to the chain) and records AUDIT_CHAIN_BACKFILLED. Idempotent: nothing to seal → sealed 0.
    [HttpPost("backfill")]
    public async Task<IActionResult> Backfill(CancellationToken cancellationToken)
    {
        var actorId = Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : (Guid?)null;
        var sealedCount = await AuditChain.BackfillAsync(_db, actorId, cancellationToken);
        var status = await AuditChain.VerifyAsync(_db, cancellationToken);
        return Ok(new { @sealed = sealedCount, status.Valid, status.BrokenAt, status.Reason });
    }
}
