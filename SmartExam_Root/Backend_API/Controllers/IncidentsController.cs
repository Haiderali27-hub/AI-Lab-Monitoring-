using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 10 — proctor incidents: what an invigilator or teacher saw during a session and wrote down from the Live
/// Monitor drawer. Anyone who may supervise the session's exam (<see cref="ExamAccessService"/>: the section's
/// teacher, an assigned proctor, an admin) may mark, escalate and resolve; students are refused by the role gate.
/// Every change is audited, and every proctor watching the exam is told at once (<c>IncidentChanged</c>), so the
/// tile badges agree across screens.
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
public class IncidentsController(AppDbContext db, ExamAccessService access, AuditService audit, IHubContext<MonitoringHub> hub)
    : ControllerBase
{
    public const string ChangedMethod = "IncidentChanged";

    private readonly AppDbContext _db = db;
    private readonly ExamAccessService _access = access;
    private readonly AuditService _audit = audit;
    private readonly IHubContext<MonitoringHub> _hub = hub;

    private Guid CurrentUserId => ExamAccessService.UserIdOf(User)!.Value;

    // POST /api/incidents  {sessionId, severity, notes} → 201
    [HttpPost("incidents")]
    public async Task<IActionResult> Create([FromBody] CreateIncidentRequest req)
    {
        var session = await _db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.SessionId == req.SessionId);
        // D-39: unknown session 404, a session of an exam the caller does not supervise 403 — nothing stored or audited.
        if (session is null) return NotFound(new { message = "Session not found." });
        if (await _access.GetAsync(User, session.ExamId) == ExamAccess.None)
            return Forbidden();

        if (!TryParseSeverity(req.Severity, out var severity))
            return BadRequest(new { code = "INVALID_SEVERITY", message = "Severity must be Low, Medium, High or Critical." });
        var notes = req.Notes?.Trim() ?? string.Empty;
        if (notes.Length == 0)
            return BadRequest(new { code = "NOTES_REQUIRED", message = "Describe what you observed." });
        if (notes.Length > Incident.MaxNotesLength)
            return BadRequest(new { code = "NOTES_TOO_LONG", message = $"Notes are limited to {Incident.MaxNotesLength} characters." });

        var now = DateTime.UtcNow;
        var incident = new Incident
        {
            SessionId = session.SessionId,
            ExamId = session.ExamId,
            CreatedBy = CurrentUserId,
            Severity = severity,
            Status = IncidentStatus.Open,
            Notes = notes,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.Incidents.Add(incident);
        _audit.Add(CurrentUserId, "INCIDENT_CREATED", "Incident", incident.IncidentId.ToString(), new
        {
            examId = session.ExamId,
            sessionId = session.SessionId,
            studentId = session.UserId,
            severity = severity.ToString(),
            notes
        });
        await _db.SaveChangesAsync();

        var view = await ViewAsync(incident.IncidentId);
        await BroadcastAsync(incident.ExamId, view!, "Created");
        return StatusCode(StatusCodes.Status201Created, view);
    }

    /// <summary>
    /// PATCH /api/incidents/{id}  {status?, notes?}. Open → Escalated, Open → Resolved, Escalated → Resolved; the notes
    /// sent with a status change are kept as <c>statusNote</c>. Without a status change the notes replace the
    /// description. Resolved is final (400 INVALID_TRANSITION), an unknown status is 400 INVALID_STATUS.
    /// </summary>
    [HttpPatch("incidents/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateIncidentRequest req)
    {
        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.IncidentId == id);
        if (incident is null || await _access.GetAsync(User, incident.ExamId) == ExamAccess.None)
            return incident is null ? NotFound(new { message = "Incident not found." }) : Forbidden();

        IncidentStatus? target = null;
        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            if (!Enum.TryParse<IncidentStatus>(req.Status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                return BadRequest(new { code = "INVALID_STATUS", message = "Status must be Open, Escalated or Resolved." });
            target = parsed;
        }

        var notes = req.Notes?.Trim();
        if (notes is { Length: > Incident.MaxNotesLength })
            return BadRequest(new { code = "NOTES_TOO_LONG", message = $"Notes are limited to {Incident.MaxNotesLength} characters." });

        if (incident.Status == IncidentStatus.Resolved)
            return BadRequest(new { code = "INVALID_TRANSITION", message = "This incident is resolved and can no longer be changed." });

        var statusChange = target is { } t && t != incident.Status;
        if (statusChange && !IsAllowed(incident.Status, target!.Value))
            return BadRequest(new { code = "INVALID_TRANSITION", message = $"An incident cannot go from {incident.Status} to {target}." });
        if (!statusChange && string.IsNullOrEmpty(notes))
            return BadRequest(new { code = "NOTHING_TO_UPDATE", message = "Send a new status or notes." });

        var now = DateTime.UtcNow;
        var from = incident.Status;
        string eventType;
        if (statusChange)
        {
            incident.Status = target!.Value;
            incident.StatusNote = string.IsNullOrEmpty(notes) ? null : notes;
            if (incident.Status == IncidentStatus.Resolved)
            {
                incident.ResolvedAt = now;
                incident.ResolvedBy = CurrentUserId;
            }
            eventType = incident.Status == IncidentStatus.Resolved ? "INCIDENT_RESOLVED" : "INCIDENT_ESCALATED";
        }
        else
        {
            incident.Notes = notes!;
            eventType = "INCIDENT_UPDATED";
        }
        incident.UpdatedAt = now;

        _audit.Add(CurrentUserId, eventType, "Incident", incident.IncidentId.ToString(), new
        {
            examId = incident.ExamId,
            sessionId = incident.SessionId,
            from = from.ToString(),
            to = incident.Status.ToString(),
            notes
        });
        await _db.SaveChangesAsync();

        var view = await ViewAsync(incident.IncidentId);
        await BroadcastAsync(incident.ExamId, view!, statusChange ? incident.Status.ToString() : "Updated");
        return Ok(view);
    }

    // GET /api/exams/{examId}/incidents?status=Open — newest first (drawer list, tile badges)
    [HttpGet("exams/{examId:guid}/incidents")]
    public async Task<IActionResult> ListForExam(Guid examId, [FromQuery] string? status = null, [FromQuery] Guid? sessionId = null)
    {
        var access = await _access.FindAsync(User, examId);
        if (access is null) return NotFound(new { message = "Exam not found." });
        if (access == ExamAccess.None) return Forbidden();

        var query = _db.Incidents.AsNoTracking().Where(i => i.ExamId == examId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var s) || !Enum.IsDefined(s))
                return BadRequest(new { code = "INVALID_STATUS", message = "Status must be Open, Escalated or Resolved." });
            query = query.Where(i => i.Status == s);
        }
        if (sessionId is Guid sid) query = query.Where(i => i.SessionId == sid);

        return Ok(await LoadViewsAsync(query));
    }

    // Open → Escalated, Open → Resolved, Escalated → Resolved. Nothing goes back to Open.
    public static bool IsAllowed(IncidentStatus from, IncidentStatus to) => (from, to) switch
    {
        (IncidentStatus.Open, IncidentStatus.Escalated) => true,
        (IncidentStatus.Open, IncidentStatus.Resolved) => true,
        (IncidentStatus.Escalated, IncidentStatus.Resolved) => true,
        _ => false
    };

    private static bool TryParseSeverity(string? value, out IncidentSeverity severity)
    {
        severity = IncidentSeverity.Medium;
        if (string.IsNullOrWhiteSpace(value)) return true;
        return Enum.TryParse(value.Trim(), ignoreCase: true, out severity) && Enum.IsDefined(severity);
    }

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new { code = "NOT_SUPERVISOR", message = "You are not assigned to supervise this exam." });

    private async Task<IncidentView?> ViewAsync(Guid incidentId) =>
        (await LoadViewsAsync(_db.Incidents.Where(i => i.IncidentId == incidentId))).FirstOrDefault();

    // Newest first. Staff names are looked up by id (CreatedBy / ResolvedBy are plain ids, like audit actors, so a
    // removed account leaves the incident intact with no name).
    private async Task<List<IncidentView>> LoadViewsAsync(IQueryable<Incident> incidents)
    {
        var rows = await (
                from i in incidents.AsNoTracking()
                join s in _db.ExamSessions on i.SessionId equals s.SessionId
                join student in _db.Users on s.UserId equals student.UserId
                select new { Incident = i, s.UserId, StudentName = student.Name })
            .ToListAsync();

        var staffIds = rows.Select(r => r.Incident.CreatedBy)
            .Concat(rows.Where(r => r.Incident.ResolvedBy.HasValue).Select(r => r.Incident.ResolvedBy!.Value))
            .Distinct()
            .ToList();
        var names = await _db.Users.AsNoTracking()
            .Where(u => staffIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.Name);

        return rows
            .OrderByDescending(r => r.Incident.CreatedAt)
            .Select(r =>
            {
                var i = r.Incident;
                return new IncidentView(
                    i.IncidentId, i.ExamId, i.SessionId, r.UserId, r.StudentName,
                    i.Severity.ToString(), i.Status.ToString(), i.Notes, i.StatusNote,
                    i.CreatedBy, names.GetValueOrDefault(i.CreatedBy), i.CreatedAt, i.UpdatedAt,
                    i.ResolvedAt, i.ResolvedBy, i.ResolvedBy is Guid by ? names.GetValueOrDefault(by) : null);
            })
            .ToList();
    }

    private Task BroadcastAsync(Guid examId, IncidentView view, string change) =>
        _hub.Clients.Group(ExamGroupTracker.GroupName(examId)).SendAsync(ChangedMethod, new { change, incident = view });
}

public record CreateIncidentRequest(Guid SessionId, string? Severity, string? Notes);

public record UpdateIncidentRequest(string? Status, string? Notes);

public record IncidentView(
    Guid IncidentId, Guid ExamId, Guid SessionId, Guid UserId, string StudentName,
    string Severity, string Status, string Notes, string? StatusNote,
    Guid CreatedBy, string? CreatedByName, DateTime CreatedAt, DateTime UpdatedAt,
    DateTime? ResolvedAt, Guid? ResolvedBy, string? ResolvedByName);
