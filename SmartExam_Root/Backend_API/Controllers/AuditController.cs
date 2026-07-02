using Backend_API.Data;
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

        var query = _db.AuditLogs.Include(a => a.Actor).AsQueryable();
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(a => a.EventType == eventType);

        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new
            {
                a.LogId,
                a.EventType,
                a.EntityType,
                a.EntityId,
                a.Details,
                a.CreatedAt,
                ActorName = a.Actor != null ? a.Actor.Name : "System",
                ActorEmail = a.Actor != null ? a.Actor.Email : null
            })
            .ToListAsync();

        return Ok(logs);
    }
}
