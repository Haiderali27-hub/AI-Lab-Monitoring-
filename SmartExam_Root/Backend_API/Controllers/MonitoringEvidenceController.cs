using System.Text.Json;
using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Backend_API.Services.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 5 / 10 — the staff side of live-monitoring evidence: a session's screen/webcam captures, its activity
/// timeline and the apps seen on the PC, plus the Live Monitor's board snapshot for one exam. Only someone who may supervise the exam (its section's teacher, an assigned proctor, an admin;
/// <see cref="ExamAccessService"/>) gets anything; students are refused by the role gate, even for their own
/// session (privacy NFR: captures are evidence for the proctors, not a self-service gallery).
/// </summary>
[ApiController]
[Route("api")]
[Authorize(Roles = "Teacher,Admin,SuperAdmin,Invigilator")]
public class MonitoringEvidenceController(AppDbContext db, ExamAccessService access, EvidenceStorage evidence) : ControllerBase
{
    public const int DefaultTimelineLimit = 200;
    public const int MaxTimelineLimit = 500;
    // Stored heartbeats read for the "apps seen" list (one per minute or per change, so this covers hours).
    public const int MaxProcessHeartbeats = 500;

    // Proctor actions on a session that the timeline shows (audit rows whose entity is the session).
    private static readonly string[] SessionActionEvents =
        ["SESSION_UNLOCKED", "SESSION_RELOCKED", "SESSION_TIME_EXTENDED", "SESSION_FORCE_SUBMITTED"];

    private readonly AppDbContext _db = db;
    private readonly ExamAccessService _access = access;
    private readonly EvidenceStorage _evidence = evidence;

    // GET /api/exams/{examId}/sessions/{sessionId}/screenshots — newest first
    [HttpGet("exams/{examId:guid}/sessions/{sessionId:guid}/screenshots")]
    public async Task<IActionResult> ListScreenshots(Guid examId, Guid sessionId, [FromQuery] string? kind = null)
    {
        if (await _access.GetAsync(User, examId) == ExamAccess.None) return Forbid();
        if (!await _db.ExamSessions.AnyAsync(s => s.SessionId == sessionId && s.ExamId == examId)) return NotFound();

        var query = _db.Screenshots.AsNoTracking().Where(s => s.SessionId == sessionId);
        if (!string.IsNullOrWhiteSpace(kind)) query = query.Where(s => s.Kind == kind);

        var rows = await query
            .OrderByDescending(s => s.CapturedAt)
            .Select(s => new
            {
                s.ScreenshotId,
                s.Kind,
                s.Trigger,
                s.TriggerReason,
                s.CapturedAt,
                s.UploadedAt,
                s.SizeBytes,
                s.Sha256,
                ContentUrl = "/api/screenshots/" + s.ScreenshotId + "/content"
            })
            .ToListAsync();
        return Ok(rows);
    }

    // GET /api/screenshots/{id}/content — the image itself (fetched as a blob by the web panel: an <img src>
    // cannot carry the bearer token)
    [HttpGet("screenshots/{screenshotId:guid}/content")]
    public async Task<IActionResult> ScreenshotContent(Guid screenshotId)
    {
        var shot = await _db.Screenshots.AsNoTracking().FirstOrDefaultAsync(s => s.ScreenshotId == screenshotId);
        if (shot is null) return NotFound();
        if (await _access.GetAsync(User, shot.ExamId) == ExamAccess.None) return Forbid();

        var stream = _evidence.OpenRead(shot.StoragePath);
        if (stream is null) return NotFound();   // expired or removed from disk

        Response.Headers.CacheControl = "private, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(stream, shot.ContentType);
    }

    /// <summary>
    /// GET /api/exams/{examId}/sessions/{sessionId}/timeline?after=&amp;limit= — everything that happened in one
    /// session, newest first: monitoring events (heartbeats excluded), captures, proctor warnings and the session's
    /// own status changes. <c>after</c> returns only items the SERVER got after that instant (incremental refresh of an
    /// open drawer): an event replayed after an outage keeps its old client time in <c>at</c> but has a fresh
    /// <c>receivedAt</c>, so it is still delivered. The caller's next <c>after</c> is the largest <c>receivedAt ?? at</c>.
    /// </summary>
    [HttpGet("exams/{examId:guid}/sessions/{sessionId:guid}/timeline")]
    public async Task<IActionResult> SessionTimeline(Guid examId, Guid sessionId, [FromQuery] DateTime? after = null, [FromQuery] int? limit = null)
    {
        if (await _access.GetAsync(User, examId) == ExamAccess.None) return Forbid();
        var session = await _db.ExamSessions.AsNoTracking().FirstOrDefaultAsync(s => s.SessionId == sessionId && s.ExamId == examId);
        if (session is null) return NotFound();

        var take = Math.Clamp(limit ?? DefaultTimelineLimit, 1, MaxTimelineLimit);
        var since = after.HasValue ? DateTime.SpecifyKind(after.Value.ToUniversalTime(), DateTimeKind.Utc) : DateTime.MinValue;
        var items = new List<TimelineItem>();

        var events = await _db.MonitoringEvents.AsNoTracking()
            .Where(e => e.ExamSessionId == sessionId && e.EventType != MonitoringEventType.Heartbeat &&
                        (e.ReceivedAt ?? e.RecordedAt) > since)
            .OrderByDescending(e => e.RecordedAt)
            .Take(take)
            .ToListAsync();
        items.AddRange(events.Select(e => new TimelineItem(
            e.RecordedAt, "Event", e.EventType.ToString(), ViolationClassifier.SeverityName(e.EventType),
            EventLabel(e.EventType), e.Payload, null, e.ReceivedAt)));

        var shots = await _db.Screenshots.AsNoTracking()
            .Where(s => s.SessionId == sessionId && s.UploadedAt > since)
            .OrderByDescending(s => s.CapturedAt)
            .Take(take)
            .ToListAsync();
        items.AddRange(shots.Select(s => new TimelineItem(
            s.CapturedAt, "Screenshot", s.Kind, s.Trigger == Screenshot.TriggerViolation ? ViolationClassifier.Warning : "Info",
            (s.Kind == Screenshot.KindWebcam ? "Webcam snapshot" : "Screen capture") +
                (s.Trigger == Screenshot.TriggerViolation ? $" (violation{(s.TriggerReason is null ? "" : ": " + s.TriggerReason)})" : ""),
            s.TriggerReason, s.ScreenshotId, s.UploadedAt)));

        // Proctor warnings sent to this student while the session was running.
        var end = session.SubmittedAt ?? DateTime.MaxValue;
        var warnings = await _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == session.UserId && n.Type == MonitoringHub.WarningType &&
                        n.CreatedAt >= session.StartedAt && n.CreatedAt <= end && n.CreatedAt > since)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();
        items.AddRange(warnings.Select(n => new TimelineItem(
            n.CreatedAt, "Warning", "ProctorWarning", ViolationClassifier.Warning, n.Title, n.Body, null, null)));

        // Incidents proctors marked on this session (current state; the audit log has each change).
        var incidents = await _db.Incidents.AsNoTracking()
            .Where(i => i.SessionId == sessionId && i.UpdatedAt > since)
            .OrderByDescending(i => i.CreatedAt)
            .Take(take)
            .ToListAsync();
        items.AddRange(incidents.Select(i => new TimelineItem(
            i.CreatedAt, "Incident", i.Status.ToString(), IncidentSeverityName(i.Severity),
            $"Incident ({i.Severity}, {i.Status})", i.Notes, null, i.UpdatedAt)));

        // Proctor actions on the session: unlocks, extra time, force-submit.
        var sid = sessionId.ToString();
        var actions = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == "ExamSession" && a.EntityId == sid && SessionActionEvents.Contains(a.EventType) &&
                        a.CreatedAt > since)
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .ToListAsync();
        items.AddRange(actions.Select(a => new TimelineItem(
            a.CreatedAt, "Action", a.EventType, "Info", ActionLabel(a.EventType), a.Details, null, null)));

        // The session's own status changes.
        if (session.StartedAt > since)
            items.Add(new TimelineItem(session.StartedAt, "Status", "Started", "Info", "Exam started", null, null, null));
        if (session.SubmittedAt is DateTime submittedAt && submittedAt > since)
            items.Add(new TimelineItem(submittedAt, "Status", session.Status.ToString(), "Info", StatusLabel(session.Status),
                session.ReceiptSha256 is null ? null : JsonSerializer.Serialize(new { answerCount = session.ReceiptAnswerCount, receiptSha256 = session.ReceiptSha256 }),
                null, null));

        return Ok(items.OrderByDescending(i => i.At).Take(take));
    }

    /// <summary>
    /// Module 10 — GET /api/exams/{examId}/live: the Live Monitor board as the server knows it, so a page that is
    /// opened (or reloaded) mid-exam starts with every student who has heartbeated instead of an empty board that
    /// fills only as new heartbeats arrive. One entry per student (their latest session of the exam), with the same
    /// fields the <c>StudentHeartbeat</c> hub event carries plus the latest screen capture and the open incidents.
    /// Live hub events take over from there.
    /// </summary>
    [HttpGet("exams/{examId:guid}/live")]
    public async Task<IActionResult> LiveSnapshot(Guid examId)
    {
        if (await _access.GetAsync(User, examId) == ExamAccess.None) return Forbid();

        var sessions = (await _db.ExamSessions.AsNoTracking()
                .Where(s => s.ExamId == examId && s.LastHeartbeatAt != null)
                .Select(s => new
                {
                    s.SessionId, s.UserId, s.Status, s.StartedAt, s.LastHeartbeatAt, s.IsDisconnected, s.DisconnectedAt,
                    s.ExtraMinutes, s.UnlockedUntil,
                    StudentName = s.Student.Name,
                    MachineName = s.Student.DeviceBinding == null ? null : s.Student.DeviceBinding.MachineName,
                    WindowsUsername = s.Student.DeviceBinding == null ? null : s.Student.DeviceBinding.WindowsUsername
                })
                .ToListAsync())
            .GroupBy(s => s.UserId)
            .Select(g => g.OrderByDescending(s => s.StartedAt).First())
            .ToList();
        var sessionIds = sessions.Select(s => s.SessionId).ToList();

        var totalQuestions = await _db.Questions.CountAsync(q => q.ExamId == examId);
        var answered = await _db.Answers.AsNoTracking()
            .Where(a => sessionIds.Contains(a.SessionId))
            .GroupBy(a => a.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count);
        var violations = (await _db.MonitoringEvents.AsNoTracking()
                .Where(e => sessionIds.Contains(e.ExamSessionId) && ViolationClassifier.ViolationTypes.Contains(e.EventType))
                .Select(e => new { e.ExamSessionId, e.EventType, e.RecordedAt, e.Payload })
                .ToListAsync())
            .ToLookup(e => e.ExamSessionId);
        var openIncidents = await _db.Incidents.AsNoTracking()
            .Where(i => i.ExamId == examId && i.Status != IncidentStatus.Resolved)
            .GroupBy(i => i.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count);
        var seats = await _db.ExamAssignments.AsNoTracking()
            .Where(a => a.ExamId == examId && a.Workstation != null)
            .Select(a => new { a.UserId, a.Workstation!.MachineNumber })
            .ToDictionaryAsync(a => a.UserId, a => a.MachineNumber);

        var now = DateTime.UtcNow;
        var board = new List<object>(sessions.Count);
        foreach (var s in sessions)
        {
            // Latest stored heartbeat and screen capture: one indexed lookup each per student (a board is ≤ a lab).
            var heartbeat = await _db.MonitoringEvents.AsNoTracking()
                .Where(e => e.ExamSessionId == s.SessionId && e.EventType == MonitoringEventType.Heartbeat)
                .OrderByDescending(e => e.RecordedAt)
                .Select(e => e.Payload)
                .FirstOrDefaultAsync();
            var shot = await _db.Screenshots.AsNoTracking()
                .Where(x => x.SessionId == s.SessionId && x.Kind == Screenshot.KindScreen)
                .OrderByDescending(x => x.CapturedAt)
                .Select(x => new { x.ScreenshotId, x.CapturedAt, x.Trigger })
                .FirstOrDefaultAsync();

            var sessionViolations = violations[s.SessionId].ToList();
            var last = sessionViolations.OrderByDescending(v => v.RecordedAt).FirstOrDefault();
            var reported = HeartbeatView.Parse(heartbeat);
            board.Add(new
            {
                userId = s.UserId,
                studentName = s.StudentName,
                workstationNumber = seats.GetValueOrDefault(s.UserId) ?? "Unassigned",
                machineName = s.MachineName,
                windowsUsername = s.WindowsUsername,
                sessionId = s.SessionId,
                sessionStatus = s.Status.ToString(),
                lastHeartbeatAt = s.LastHeartbeatAt,
                isDisconnected = s.IsDisconnected,
                disconnectedAt = s.DisconnectedAt,
                extraMinutes = s.ExtraMinutes,
                unlockedUntilUtc = s.UnlockedUntil > now ? s.UnlockedUntil : null,
                answeredCount = answered.GetValueOrDefault(s.SessionId),
                totalQuestions,
                violationCount = sessionViolations.Count,
                tileStatus = ViolationClassifier.Classify(sessionViolations.Select(v => v.EventType)),
                lastViolationType = last?.EventType.ToString(),
                lastViolationPayload = last?.Payload,
                activeWindow = reported.ActiveWindowTitle,
                isForegroundExamApp = reported.IsForegroundExamApp,
                processes = reported.ProcessListSnapshot,
                idleSeconds = reported.IdleSeconds,
                cameraStatus = reported.CameraStatus,
                latestScreenshot = shot,
                openIncidents = openIncidents.GetValueOrDefault(s.SessionId)
            });
        }

        return Ok(new { examId, serverTimeUtc = now, students = board });
    }

    /// <summary>
    /// GET /api/exams/{examId}/sessions/{sessionId}/processes — the apps the PC reported during the session: what is
    /// running now (latest heartbeat) and every app seen so far with first/last sighting, so an app opened and closed
    /// between two looks at the drawer still shows up.
    /// </summary>
    [HttpGet("exams/{examId:guid}/sessions/{sessionId:guid}/processes")]
    public async Task<IActionResult> SessionProcesses(Guid examId, Guid sessionId)
    {
        if (await _access.GetAsync(User, examId) == ExamAccess.None) return Forbid();
        if (!await _db.ExamSessions.AnyAsync(s => s.SessionId == sessionId && s.ExamId == examId)) return NotFound();

        var beats = await _db.MonitoringEvents.AsNoTracking()
            .Where(e => e.ExamSessionId == sessionId && e.EventType == MonitoringEventType.Heartbeat)
            .OrderByDescending(e => e.RecordedAt)
            .Take(MaxProcessHeartbeats)
            .Select(e => new { e.RecordedAt, e.Payload })
            .ToListAsync();

        var seen = new Dictionary<string, (string Name, DateTime First, DateTime Last)>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> current = [];
        DateTime? currentAt = null;
        foreach (var beat in beats.OrderBy(b => b.RecordedAt))
        {
            var snapshot = HeartbeatView.Parse(beat.Payload).ProcessListSnapshot;
            if (string.IsNullOrWhiteSpace(snapshot) || snapshot == "UNAVAILABLE") continue;
            var names = snapshot.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var name in names)
                seen[name] = seen.TryGetValue(name, out var known) ? (known.Name, known.First, beat.RecordedAt) : (name, beat.RecordedAt, beat.RecordedAt);
            current = names;
            currentAt = beat.RecordedAt;
        }

        var running = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        return Ok(new
        {
            current = current.OrderBy(n => n, StringComparer.OrdinalIgnoreCase),
            currentAt,
            seen = seen.Values
                .OrderByDescending(v => running.Contains(v.Name))
                .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .Select(v => new { name = v.Name, firstSeenAt = v.First, lastSeenAt = v.Last, running = running.Contains(v.Name) })
        });
    }

    private static string IncidentSeverityName(IncidentSeverity severity) => severity switch
    {
        IncidentSeverity.Low => ViolationClassifier.Warning,
        IncidentSeverity.Medium => ViolationClassifier.Suspicious,
        _ => ViolationClassifier.Violation
    };

    private static string ActionLabel(string eventType) => eventType switch
    {
        "SESSION_UNLOCKED" => "Lockdown unlocked by a proctor",
        "SESSION_RELOCKED" => "Lockdown restored by a proctor",
        "SESSION_TIME_EXTENDED" => "Extra time granted",
        "SESSION_FORCE_SUBMITTED" => "Force-submitted by a proctor",
        _ => eventType
    };

    // The stored heartbeat payload (MonitoringController.Heartbeat), PascalCase.
    private sealed record HeartbeatView(bool? IsForegroundExamApp, string? ActiveWindowTitle, string? ProcessListSnapshot,
        string? CameraStatus, int? IdleSeconds)
    {
        private static readonly HeartbeatView Empty = new(null, null, null, null, null);

        public static HeartbeatView Parse(string? payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return Empty;
            try
            {
                return JsonSerializer.Deserialize<HeartbeatView>(payload) ?? Empty;
            }
            catch (JsonException)
            {
                return Empty;
            }
        }
    }

    private static string StatusLabel(SessionStatus status) => status switch
    {
        SessionStatus.Submitted => "Submitted",
        SessionStatus.ForceSubmitted => "Force submitted",
        SessionStatus.TimedOut => "Timed out",
        _ => "In progress"
    };

    /// <summary>Human-readable label for an event type (the web panel shows the payload detail next to it).</summary>
    public static string EventLabel(MonitoringEventType type) => type switch
    {
        MonitoringEventType.FocusLost => "Left the exam window",
        MonitoringEventType.KeyBlocked => "Blocked keys",
        MonitoringEventType.ClipboardBlocked => "Clipboard blocked",
        MonitoringEventType.UnauthorizedProcess => "Unauthorized app",
        MonitoringEventType.ProcessKilled => "Unauthorized app closed",
        MonitoringEventType.UsbInserted => "USB storage inserted",
        MonitoringEventType.MultiMonitor => "Extra monitor connected",
        MonitoringEventType.RemoteSession => "Remote desktop session",
        MonitoringEventType.VirtualMachine => "Virtual machine",
        MonitoringEventType.LockdownDegraded => "Lockdown degraded",
        MonitoringEventType.SeatMismatch => "Seat mismatch",
        MonitoringEventType.BackupTampered => "Local backup tampered",
        MonitoringEventType.Disconnected => "Disconnected",
        MonitoringEventType.Reconnected => "Reconnected",
        MonitoringEventType.NetworkOffline => "Network outage on the PC",
        MonitoringEventType.CameraCovered => "Camera covered",
        MonitoringEventType.CameraFrozen => "Camera feed frozen",
        MonitoringEventType.CameraUnavailable => "Camera unavailable",
        MonitoringEventType.NoFace => "No face in view",
        MonitoringEventType.MultipleFaces => "Multiple faces in view",
        MonitoringEventType.LookingAway => "Looking away from the screen",
        MonitoringEventType.PhoneDetected => "Mobile phone detected",
        MonitoringEventType.ProcessList => "Running apps changed",
        MonitoringEventType.LockdownSuspended => "Lockdown suspended (proctor unlock)",
        MonitoringEventType.LockdownResumed => "Lockdown resumed",
        MonitoringEventType.WindowFocus => "Activity",
        MonitoringEventType.Violation => "Violation",
        _ => type.ToString()
    };

    /// <param name="At">When it happened (client time for client events).</param>
    /// <param name="Kind">Event | Screenshot | Warning | Incident | Action | Status.</param>
    /// <param name="Severity">Info | Warning | Suspicious | Violation.</param>
    /// <param name="Detail">Event payload JSON, the violation reason of a violation capture, warning text, incident notes, proctor-action details JSON, or receipt JSON.</param>
    /// <param name="ReceivedAt">When the server got it (events, captures; differs from At for an event replayed after an
    /// outage; for an incident, its last change). Null for warnings, actions and status items, whose At is already server time. <c>?after=</c> compares
    /// <c>ReceivedAt ?? At</c>.</param>
    public sealed record TimelineItem(DateTime At, string Kind, string Type, string Severity, string Label, string? Detail,
        Guid? ScreenshotId, DateTime? ReceivedAt);
}
