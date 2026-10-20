using System.Text.Json;
using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Backend_API.Services.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Module 5 — the staff side of live-monitoring evidence: a session's screen/webcam captures and its activity
/// timeline. Only someone who may supervise the exam (its section's teacher, an assigned proctor, an admin;
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
            null, s.ScreenshotId, s.UploadedAt)));

        // Proctor warnings sent to this student while the session was running.
        var end = session.SubmittedAt ?? DateTime.MaxValue;
        var warnings = await _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == session.UserId && n.Type == "Warning" &&
                        n.CreatedAt >= session.StartedAt && n.CreatedAt <= end && n.CreatedAt > since)
            .OrderByDescending(n => n.CreatedAt)
            .Take(take)
            .ToListAsync();
        items.AddRange(warnings.Select(n => new TimelineItem(
            n.CreatedAt, "Warning", "ProctorWarning", ViolationClassifier.Warning, n.Title, n.Body, null, null)));

        // The session's own status changes.
        if (session.StartedAt > since)
            items.Add(new TimelineItem(session.StartedAt, "Status", "Started", "Info", "Exam started", null, null, null));
        if (session.SubmittedAt is DateTime submittedAt && submittedAt > since)
            items.Add(new TimelineItem(submittedAt, "Status", session.Status.ToString(), "Info", StatusLabel(session.Status),
                session.ReceiptSha256 is null ? null : JsonSerializer.Serialize(new { answerCount = session.ReceiptAnswerCount, receiptSha256 = session.ReceiptSha256 }),
                null, null));

        return Ok(items.OrderByDescending(i => i.At).Take(take));
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
        MonitoringEventType.ProcessList => "Running apps changed",
        MonitoringEventType.WindowFocus => "Activity",
        MonitoringEventType.Violation => "Violation",
        _ => type.ToString()
    };

    /// <param name="At">When it happened (client time for client events).</param>
    /// <param name="Kind">Event | Screenshot | Warning | Status.</param>
    /// <param name="Severity">Info | Warning | Suspicious | Violation.</param>
    /// <param name="Detail">Event payload JSON, warning text, or receipt JSON.</param>
    /// <param name="ReceivedAt">When the server got it (events, captures; differs from At for an event replayed after an
    /// outage). Null for warnings and status items, whose At is already server time. <c>?after=</c> compares
    /// <c>ReceivedAt ?? At</c>.</param>
    public sealed record TimelineItem(DateTime At, string Kind, string Type, string Severity, string Label, string? Detail,
        Guid? ScreenshotId, DateTime? ReceivedAt);
}
