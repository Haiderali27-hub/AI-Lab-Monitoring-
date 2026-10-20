using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend_API.Data;
using Backend_API.DTOs;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Backend_API.Services.Monitoring;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/monitoring")]
[Authorize(Roles = "Student")]
public class MonitoringController(
    AppDbContext db,
    IHubContext<MonitoringHub> hubContext,
    SeatGuard seats,
    EvidenceStorage evidence,
    IConfiguration configuration,
    ILogger<MonitoringController> logger) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly IHubContext<MonitoringHub> _hubContext = hubContext;
    private readonly SeatGuard _seats = seats;
    private readonly EvidenceStorage _evidence = evidence;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<MonitoringController> _logger = logger;

    // A student sitting at the wrong seat heartbeats every 10 s; record the violation once per
    // (session, seat actually used) in this window instead of on every beat.
    public static readonly TimeSpan SeatMismatchDedupWindow = TimeSpan.FromMinutes(5);

    // Module 5: a heartbeat is stored as an event only when what it reports changed, or once a minute
    // (30 students × 6 beats a minute would otherwise write ~10 800 rows an hour). Liveness itself is
    // ExamSession.LastHeartbeatAt, updated on every beat.
    public static readonly TimeSpan HeartbeatPersistInterval = TimeSpan.FromSeconds(60);

    // Module 5 batch limits and client-clock sanity window.
    public const int MaxBatchEvents = 200;
    public const int MaxEventPayloadChars = 8 * 1024;
    public static readonly TimeSpan MaxClientClockAhead = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan MaxClientEventAge = TimeSpan.FromHours(24);
    // Replayed violations older than this are stored but not pushed live (no alert storm after an outage).
    public static readonly TimeSpan LiveBroadcastWindow = TimeSpan.FromSeconds(60);

    // Module 5 screenshot upload limits. The client sends one every 30–90 s plus one per violation
    // (rate-limited to one per 15 s), so 30 a minute is far above honest use.
    public const string MaxScreenshotsPerMinuteKey = "Monitoring:MaxScreenshotsPerMinute";
    public const int DefaultMaxScreenshotsPerMinute = 30;
    private const long MultipartLimit = 16L * 1024 * 1024;

    // Module 10: at most this many unread proctor warnings ride on one heartbeat ack.
    public const int MaxWarningsPerAck = 5;

    // Webcam state from the desktop: the picture (Covered/Frozen/Unavailable) or, S8, the on-device models'
    // findings (NoFace/MultipleFaces/LookingAway/Phone). Anything else is dropped (null).
    private static readonly string[] CameraStatuses =
        ["Ok", "Covered", "Frozen", "Unavailable", "Off", "NoFace", "MultipleFaces", "LookingAway", "Phone"];
    private const int MaxProcessSnapshotChars = 4000;
    private const int MaxWindowTitleChars = 512;

    private Guid CallerId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatPayload request)
    {
        // D-45: exam monitoring comes from the desktop app only (a web token carries no device, seat or lockdown).
        if (_seats.RefusesClient(User)) return SeatGuard.DesktopRequired();
        var studentId = CallerId;
        var now = DateTime.UtcNow;

        var processes = Truncate(request.ProcessListSnapshot, MaxProcessSnapshotChars);
        var windowTitle = Truncate(request.ActiveWindowTitle, MaxWindowTitleChars);
        var cameraStatus = CameraStatuses.FirstOrDefault(s => string.Equals(s, request.CameraStatus, StringComparison.OrdinalIgnoreCase));
        var idleSeconds = request.IdleSeconds is int idle ? Math.Clamp(idle, 0, 86_400) : (int?)null;

        var payload = JsonSerializer.Serialize(new
        {
            request.IsForegroundExamApp,
            ActiveWindowTitle = windowTitle,
            ProcessListSnapshot = processes,
            CameraStatus = cameraStatus,
            IdleSeconds = idleSeconds
        });

        // Heartbeats can legitimately arrive before/without an active session (no session id): those still update
        // the device's liveness, just aren't stored as session events. A heartbeat that NAMES a session must name one
        // of the caller's own (D-06): an unknown or another student's session is refused with 404 — the same answer
        // for both, so session ids cannot be probed — and nothing is recorded (no event, no liveness, no broadcast).
        ExamSession? session = null;
        if (request.ExamSessionId is Guid namedSession && namedSession != Guid.Empty)
        {
            session = await _db.ExamSessions
                .Include(s => s.Exam)
                .FirstOrDefaultAsync(s => s.SessionId == namedSession && s.UserId == studentId);
            if (session is null)
                return NotFound(new ApiEnvelope<object>(false, "NOT_FOUND", "Session not found.", null));
        }

        int? reconnectedAfterSeconds = null;
        if (session is not null)
        {
            if (await ShouldPersistHeartbeatAsync(session.SessionId, payload, now))
            {
                _db.MonitoringEvents.Add(new MonitoringEvent
                {
                    EventId = Guid.NewGuid(),
                    ExamSessionId = session.SessionId,
                    EventType = MonitoringEventType.Heartbeat,
                    Payload = payload,
                    RecordedAt = now,
                    ReceivedAt = now
                });
            }

            // Module 5: liveness. A session the watchdog flagged comes back with this beat.
            reconnectedAfterSeconds = MarkAlive(session, now);
        }

        // Module 9: the seat is re-checked on every heartbeat, not only at start.
        ExamAssignment? assignment = null;
        SeatCheckResult? seat = null;
        if (session is not null)
        {
            assignment = await _db.ExamAssignments
                .Include(a => a.Exam)
                .Include(a => a.Workstation)
                .FirstOrDefaultAsync(a => a.ExamId == session.ExamId && a.UserId == studentId);
            if (assignment is not null)
            {
                seat = _seats.Check(assignment, User, HttpContext);
                // Only a running session is flagged (a late heartbeat after submit is not a violation).
                if (seat.IsMismatch && session.Status == SessionStatus.InProgress)
                {
                    var mismatchPayload = SeatGuard.MismatchPayload(seat, "heartbeat");
                    var since = now - SeatMismatchDedupWindow;
                    var alreadyRecorded = await _db.MonitoringEvents.AnyAsync(e =>
                        e.ExamSessionId == session.SessionId &&
                        e.EventType == MonitoringEventType.SeatMismatch &&
                        e.RecordedAt >= since &&
                        e.Payload == mismatchPayload);
                    if (!alreadyRecorded)
                    {
                        _db.MonitoringEvents.Add(new MonitoringEvent
                        {
                            EventId = Guid.NewGuid(),
                            ExamSessionId = session.SessionId,
                            EventType = MonitoringEventType.SeatMismatch,
                            Payload = mismatchPayload,
                            RecordedAt = now,
                            ReceivedAt = now
                        });
                        await _seats.ReportMismatchAsync(session.ExamId, studentId, session.SessionId, seat, "heartbeat");
                    }
                }
            }
        }

        var user = await _db.Users
            .Include(u => u.DeviceBinding)
            .FirstOrDefaultAsync(u => u.UserId == studentId);

        if (user?.DeviceBinding is not null)
        {
            user.DeviceBinding.LastSeenAt = now;
        }

        await SaveHeartbeatAsync(session, now, reconnectedAfterSeconds is not null);

        var workstationNumber = "XX";
        var answeredCount = 0;
        var totalQuestions = 0;
        var violationCount = 0;
        var violationTypes = new List<MonitoringEventType>();
        int? remainingSeconds = null;

        if (session is not null)
        {
            answeredCount = await _db.Answers.CountAsync(a => a.SessionId == session.SessionId);
            totalQuestions = await _db.Questions.CountAsync(q => q.ExamId == session.ExamId);
            violationTypes = await _db.MonitoringEvents
                .Where(e => e.ExamSessionId == session.SessionId && ViolationClassifier.ViolationTypes.Contains(e.EventType))
                .Select(e => e.EventType)
                .ToListAsync();
            violationCount = violationTypes.Count;

            // Authoritative remaining time — lets the client resync its countdown after a teacher extends
            // the exam or a proctor grants this student extra minutes (Module 6: per-student deadline).
            remainingSeconds = SubmissionService.RemainingSeconds(session, now);

            if (assignment?.Workstation is not null)
            {
                workstationNumber = assignment.Workstation.MachineNumber;
            }
        }

        var tileStatus = ViolationClassifier.Classify(violationTypes);

        // Broadcast to the proctors watching THIS exam only (was Clients.All)
        if (session is not null)
        {
            var group = ExamGroupTracker.GroupName(session.ExamId);
            if (reconnectedAfterSeconds is int offline)
            {
                // Sent before the heartbeat so the tile leaves "Disconnected" and then shows the fresh state.
                await _hubContext.Clients.Group(group).SendAsync("StudentReconnected", new
                {
                    userId = studentId,
                    sessionId = session.SessionId,
                    examId = session.ExamId,
                    offlineSeconds = offline,
                    reconnectedAt = now
                });
            }

            await _hubContext.Clients.Group(group).SendAsync(
                "StudentHeartbeat",
                new
                {
                    userId = studentId,
                    studentName = user?.Name ?? "Student",
                    workstationNumber,
                    machineName = user?.DeviceBinding?.MachineName,          // real Windows PC name
                    windowsUsername = user?.DeviceBinding?.WindowsUsername,  // Windows account in use
                    sessionId = request.ExamSessionId,
                    activeWindow = windowTitle ?? "Unknown",
                    isForegroundExamApp = request.IsForegroundExamApp,
                    answeredCount,
                    totalQuestions,
                    violationCount,
                    tileStatus,
                    // Module 9: Ok | Mismatch | Unassigned | NotEnforced | NotChecked
                    seatStatus = seat?.Status.ToString() ?? nameof(SeatStatus.Unassigned),
                    expectedSeat = seat?.ExpectedSeat,
                    actualMachine = seat?.ActualMachine,
                    seatDetail = seat is { IsMismatch: true } ? seat.Detail : null,
                    sessionStatus = session.Status.ToString(),
                    // Module 5: what the PC is running, input idle time, webcam state, liveness
                    processes,
                    idleSeconds,
                    cameraStatus,
                    lastHeartbeatAt = now,
                    isDisconnected = false,
                    // Module 6 / 10: per-student extra time and a proctor's lockdown unlock, so every board agrees
                    extraMinutes = session.ExtraMinutes,
                    unlockedUntilUtc = session.UnlockedUntil > now ? session.UnlockedUntil : null
                });
        }

        // Module 10: the ack is the desktop client's command channel (it has no SignalR connection). A running
        // session gets the proctors' unread warnings (D-05; shown until the student acknowledges them) and the
        // lockdown unlock deadline, if a proctor unlocked the PC (the client re-locks on its own when it passes).
        DateTime? unlockedUntilUtc = null;
        var warnings = new List<ProctorWarningAck>();
        if (session is { Status: SessionStatus.InProgress })
        {
            if (session.UnlockedUntil is DateTime until && until > now) unlockedUntilUtc = until;
            warnings = await PendingWarningsAsync(session);
        }

        // sessionStatus lets the desktop client close the exam window when a proctor force-submitted the
        // session or the timeout sweeper ended it (otherwise the student would sit locked down until the timer).
        // The capture settings let the client follow a teacher's change mid-exam (Module 5).
        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new
        {
            receivedAtUtc = now,
            remainingSeconds,
            sessionStatus = session?.Status.ToString(),
            extraMinutes = session?.ExtraMinutes,
            screenshotIntervalSeconds = session?.Exam.ScreenshotIntervalSeconds,
            webcamIntervalSeconds = session?.Exam.WebcamIntervalSeconds,
            requireWebcam = session?.Exam.RequireWebcam,
            unlockedUntilUtc,
            warnings
        }));
    }

    // Unread proctor warnings sent to this student since the session started, oldest first (the client shows them in
    // order and acknowledges each with PATCH /api/notifications/{id}/read, after which they stop coming).
    private Task<List<ProctorWarningAck>> PendingWarningsAsync(ExamSession session) =>
        _db.Notifications.AsNoTracking()
            .Where(n => n.RecipientId == session.UserId && n.Type == MonitoringHub.WarningType && !n.IsRead &&
                        n.CreatedAt >= session.StartedAt)
            .OrderBy(n => n.CreatedAt)
            .Take(MaxWarningsPerAck)
            .Select(n => new ProctorWarningAck(n.NotificationId, n.Title, n.Body, n.CreatedAt))
            .ToListAsync();

    public sealed record ProctorWarningAck(Guid NotificationId, string Title, string Body, DateTime CreatedAt);

    // Stamps liveness; returns the offline time when the session had been flagged disconnected.
    private int? MarkAlive(ExamSession session, DateTime now)
    {
        var previous = session.LastHeartbeatAt ?? session.StartedAt;
        session.LastHeartbeatAt = now;
        if (!session.IsDisconnected) return null;

        var offlineSeconds = Math.Max(0, (int)(now - previous).TotalSeconds);
        _db.MonitoringEvents.Add(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            ExamSessionId = session.SessionId,
            EventType = MonitoringEventType.Reconnected,
            Payload = JsonSerializer.Serialize(new { offlineSeconds, disconnectedAt = session.DisconnectedAt }),
            RecordedAt = now,
            ReceivedAt = now
        });
        session.IsDisconnected = false;
        session.DisconnectedAt = null;
        return offlineSeconds;
    }

    // ExamSession.Status is a concurrency token. A heartbeat that lands in the same instant as a submit,
    // force-submit or timeout would otherwise fail on its liveness update: reload and re-apply it once. The reload
    // discards everything this beat set on the session, so a reconnect is re-applied too — otherwise the Reconnected
    // event (still pending) would be stored while the session stayed flagged, and the next beat would record a
    // second reconnect.
    private async Task SaveHeartbeatAsync(ExamSession? session, DateTime now, bool reconnected)
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException) when (session is not null)
        {
            await _db.Entry(session).ReloadAsync();
            session.LastHeartbeatAt = now;
            if (reconnected)
            {
                session.IsDisconnected = false;
                session.DisconnectedAt = null;
            }
            await _db.SaveChangesAsync();
        }
    }

    private async Task<bool> ShouldPersistHeartbeatAsync(Guid sessionId, string payload, DateTime now)
    {
        var last = await _db.MonitoringEvents.AsNoTracking()
            .Where(e => e.ExamSessionId == sessionId && e.EventType == MonitoringEventType.Heartbeat)
            .OrderByDescending(e => e.RecordedAt)
            .Select(e => new { e.RecordedAt, e.Payload })
            .FirstOrDefaultAsync();
        return last is null
               || now - last.RecordedAt >= HeartbeatPersistInterval
               || HeartbeatSignature(last.Payload) != HeartbeatSignature(payload);
    }

    // What a heartbeat reports, minus the idle counter (which changes on every beat).
    private static string HeartbeatSignature(string payload)
    {
        try
        {
            var node = JsonNode.Parse(payload) as JsonObject;
            if (node is null) return payload;
            node.Remove("IdleSeconds");
            return node.ToJsonString();
        }
        catch (JsonException)
        {
            return payload;
        }
    }

    [HttpPost("event")]
    public async Task<IActionResult> PushEvent([FromBody] MonitoringEventPayload request)
    {
        // D-45: exam monitoring comes from the desktop app only (a web token carries no device, seat or lockdown).
        if (_seats.RefusesClient(User)) return SeatGuard.DesktopRequired();
        var studentId = CallerId;
        var now = DateTime.UtcNow;

        // "Violation" + payload.reason, or a specific type name ("ProcessKilled"), both accepted.
        var eventType = ViolationClassifier.Resolve(request.EventType, request.PayloadJson);

        var hasSessionRef = request.ExamSessionId.HasValue && request.ExamSessionId.Value != Guid.Empty;
        if (hasSessionRef)
        {
            // A specific session was named — it MUST belong to the caller. This blocks a
            // student from injecting events (e.g. framing a peer with a Violation) into
            // another student's session (IDOR).
            var session = await _db.ExamSessions
                .FirstOrDefaultAsync(s => s.SessionId == request.ExamSessionId!.Value && s.UserId == studentId);
            if (session is null)
                return NotFound(new ApiEnvelope<object>(false, "NOT_FOUND", "Session not found.", null));

            _db.MonitoringEvents.Add(new MonitoringEvent
            {
                EventId = Guid.NewGuid(),
                ExamSessionId = session.SessionId,
                EventType = eventType,
                Payload = request.PayloadJson,
                RecordedAt = now,
                ReceivedAt = now
            });
            await _db.SaveChangesAsync();

            if (ViolationClassifier.IsViolation(eventType))
            {
                // Scoped to the proctors watching this exam (was Clients.All)
                await _hubContext.Clients.Group(ExamGroupTracker.GroupName(session.ExamId)).SendAsync(
                    "ViolationEvent",
                    new
                    {
                        userId = studentId,
                        eventType = request.EventType,
                        violationType = eventType.ToString(),
                        payload = request.PayloadJson
                    });
            }
        }
        // No session reference (e.g. pre-session attendance/identity check-in): accept as a
        // no-op so the client flow isn't blocked, but nothing is persisted.

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new { receivedAtUtc = now }));
    }

    /// <summary>
    /// Module 5 — events the desktop client queued (on disk, encrypted) and flushes every 5 s, or at once for a
    /// violation; after an outage the whole backlog arrives here in order. Idempotent: an event whose
    /// <c>clientEventId</c> this session already has is skipped (a batch resent after a lost reply is stored once).
    /// <c>recordedAt</c> is the client's time and is kept, unless it is absurd (over 2 min ahead of the server or
    /// over 24 h old), in which case the server time is used and the payload says so.
    /// </summary>
    [HttpPost("events/batch")]
    public async Task<IActionResult> PushEventBatch([FromBody] MonitoringEventBatch request)
    {
        // D-45: exam monitoring comes from the desktop app only (a web token carries no device, seat or lockdown).
        if (_seats.RefusesClient(User)) return SeatGuard.DesktopRequired();
        var studentId = CallerId;
        var now = DateTime.UtcNow;
        var events = request.Events ?? [];

        if (events.Count > MaxBatchEvents)
            return BadRequest(new ApiEnvelope<object>(false, "BATCH_TOO_LARGE", $"At most {MaxBatchEvents} events per batch.", null));

        var session = await _db.ExamSessions
            .FirstOrDefaultAsync(s => s.SessionId == request.ExamSessionId && s.UserId == studentId);
        if (session is null)
            return NotFound(new ApiEnvelope<object>(false, "NOT_FOUND", "Session not found.", null));

        // Validate everything before storing anything: a batch is all or nothing.
        var prepared = new List<(Guid? ClientId, MonitoringEvent Event, string RawType)>(events.Count);
        foreach (var e in events)
        {
            if (string.IsNullOrWhiteSpace(e.EventType) || e.EventType.Length > 64)
                return BadRequest(new ApiEnvelope<object>(false, "INVALID_EVENT", "Every event needs an eventType.", null));
            var payloadJson = string.IsNullOrWhiteSpace(e.PayloadJson) ? "{}" : e.PayloadJson;
            if (payloadJson.Length > MaxEventPayloadChars)
                return BadRequest(new ApiEnvelope<object>(false, "PAYLOAD_TOO_LARGE", $"An event payload may be at most {MaxEventPayloadChars} characters.", null));

            JsonNode? payloadNode;
            try
            {
                payloadNode = JsonNode.Parse(payloadJson);
            }
            catch (JsonException)
            {
                return BadRequest(new ApiEnvelope<object>(false, "INVALID_PAYLOAD", "payloadJson must be valid JSON.", null));
            }

            var recordedAt = e.RecordedAt is DateTime at ? DateTime.SpecifyKind(at.ToUniversalTime(), DateTimeKind.Utc) : now;
            if (e.RecordedAt is null || recordedAt > now + MaxClientClockAhead || recordedAt < now - MaxClientEventAge)
            {
                if (payloadNode is JsonObject obj)
                {
                    obj["clockAdjusted"] = true;
                    obj["clientRecordedAt"] = e.RecordedAt;
                    payloadJson = obj.ToJsonString();
                }
                recordedAt = now;
            }

            var clientId = e.ClientEventId is Guid g && g != Guid.Empty ? g : (Guid?)null;
            prepared.Add((clientId, new MonitoringEvent
            {
                EventId = Guid.NewGuid(),
                ExamSessionId = session.SessionId,
                EventType = ViolationClassifier.Resolve(e.EventType, payloadJson),
                Payload = payloadJson,
                RecordedAt = recordedAt,
                ClientEventId = clientId,
                ReceivedAt = now
            }, e.EventType));
        }

        // Dedup in code (the InMemory provider ignores unique indexes); the partial unique index on
        // (ExamSessionId, ClientEventId) is the backstop on Postgres for two identical batches racing.
        List<(Guid? ClientId, MonitoringEvent Event, string RawType)> fresh = [];
        var duplicates = 0;
        for (var attempt = 0; ; attempt++)
        {
            var ids = prepared.Where(p => p.ClientId.HasValue).Select(p => p.ClientId!.Value).Distinct().ToList();
            var known = ids.Count == 0
                ? new HashSet<Guid>()
                : (await _db.MonitoringEvents.AsNoTracking()
                    .Where(m => m.ExamSessionId == session.SessionId && m.ClientEventId != null && ids.Contains(m.ClientEventId.Value))
                    .Select(m => m.ClientEventId!.Value)
                    .ToListAsync()).ToHashSet();

            fresh = [];
            duplicates = 0;
            var seen = new HashSet<Guid>();
            foreach (var p in prepared)
            {
                if (p.ClientId is Guid id && (known.Contains(id) || !seen.Add(id)))
                {
                    duplicates++;
                    continue;
                }
                fresh.Add(p);
            }

            _db.MonitoringEvents.AddRange(fresh.Select(p => p.Event));
            try
            {
                await _db.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (attempt == 0)
            {
                // Another copy of this batch committed first: forget our inserts and dedup again.
                _logger.LogInformation(ex, "Event batch for session {SessionId} raced a duplicate; retrying dedup.", session.SessionId);
                foreach (var p in fresh) _db.Entry(p.Event).State = EntityState.Detached;
            }
        }

        // Live alerts only for fresh violations of a running session; an offline backlog shows up in the
        // counts with the next heartbeat and in the timeline, without a burst of alerts.
        if (session.Status == SessionStatus.InProgress)
        {
            foreach (var p in fresh.Where(p => ViolationClassifier.IsViolation(p.Event.EventType) && now - p.Event.RecordedAt <= LiveBroadcastWindow))
            {
                await _hubContext.Clients.Group(ExamGroupTracker.GroupName(session.ExamId)).SendAsync(
                    "ViolationEvent",
                    new
                    {
                        userId = studentId,
                        eventType = p.RawType,
                        violationType = p.Event.EventType.ToString(),
                        payload = p.Event.Payload,
                        recordedAt = p.Event.RecordedAt
                    });
            }
        }

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new
        {
            accepted = fresh.Count,
            duplicates,
            receivedAtUtc = now
        }));
    }

    /// <summary>
    /// Module 5 — one capture (screen: all monitors, or a webcam snapshot) from the desktop client: every 30–90 s
    /// (random) and immediately on a violation. Multipart: <c>examSessionId</c>, <c>file</c>, optional <c>kind</c>,
    /// <c>capturedAt</c>, <c>trigger</c>, <c>reason</c>. Only a JPEG or PNG (decided by the file's first bytes) of at
    /// most 10 MB, for the caller's own running session, is accepted; it is stored under a server-generated name and
    /// announced to the exam's proctors as <c>ScreenshotUploaded</c>.
    /// </summary>
    [HttpPost("screenshot")]
    [HttpPost("screenshots")]
    [RequestSizeLimit(MultipartLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = MultipartLimit)]
    public async Task<IActionResult> UploadScreenshot([FromForm] ScreenshotUploadForm form, CancellationToken cancellationToken)
    {
        // D-45: exam monitoring comes from the desktop app only (a web token carries no device, seat or lockdown).
        if (_seats.RefusesClient(User)) return SeatGuard.DesktopRequired();
        var studentId = CallerId;
        var now = DateTime.UtcNow;

        var file = form.File;
        if (file is null || file.Length == 0)
            return BadRequest(new ApiEnvelope<object>(false, "FILE_REQUIRED", "An image file is required.", null));
        // TestServer and some proxies do not enforce RequestSizeLimit, so the limit is also checked here.
        if (file.Length > EvidenceStorage.MaxUploadBytes)
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new ApiEnvelope<object>(false, "FILE_TOO_LARGE", "A capture may be at most 10 MB.", null));

        var kind = form.Kind is null ? Screenshot.KindScreen
            : Screenshot.Kinds.FirstOrDefault(k => string.Equals(k, form.Kind, StringComparison.OrdinalIgnoreCase));
        var trigger = form.Trigger is null ? Screenshot.TriggerInterval
            : Screenshot.Triggers.FirstOrDefault(t => string.Equals(t, form.Trigger, StringComparison.OrdinalIgnoreCase));
        if (kind is null || trigger is null)
            return BadRequest(new ApiEnvelope<object>(false, "INVALID_KIND", "kind must be Screen or Webcam; trigger Interval, Violation or Manual.", null));

        var session = await _db.ExamSessions
            .Include(s => s.Exam)
            .FirstOrDefaultAsync(s => s.SessionId == form.ExamSessionId && s.UserId == studentId, cancellationToken);
        if (session is null)
            return NotFound(new ApiEnvelope<object>(false, "NOT_FOUND", "Session not found.", null));
        // Capture only while the exam is actually being written (privacy NFR).
        if (session.Status != SessionStatus.InProgress || SubmissionService.IsClosed(session, now))
            return Conflict(new ApiEnvelope<object>(false, "SESSION_NOT_ACTIVE", "This exam session is not in progress.", null));
        // A webcam image is only kept for an exam that requires the webcam.
        if (kind == Screenshot.KindWebcam && !session.Exam.RequireWebcam)
            return BadRequest(new ApiEnvelope<object>(false, "WEBCAM_NOT_REQUIRED", "This exam does not use the webcam.", null));

        var perMinute = _configuration.GetValue(MaxScreenshotsPerMinuteKey, DefaultMaxScreenshotsPerMinute);
        var minuteAgo = now.AddMinutes(-1);
        if (perMinute > 0 && await _db.Screenshots.CountAsync(s => s.SessionId == session.SessionId && s.UploadedAt >= minuteAgo, cancellationToken) >= perMinute)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new ApiEnvelope<object>(false, "TOO_MANY_CAPTURES", "Too many captures for this session; try again shortly.", null));

        // The first bytes decide what the file is; the declared content type and file name are ignored.
        var head = new byte[8];
        int headLength;
        await using (var probe = file.OpenReadStream())
            headLength = await probe.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, cancellationToken);
        var contentType = EvidenceStorage.DetectImageType(head.AsSpan(0, headLength));
        if (contentType is null)
            return BadRequest(new ApiEnvelope<object>(false, "INVALID_IMAGE", "Only JPEG or PNG images are accepted.", null));
        await using var stream = file.OpenReadStream();

        var capturedAt = ParseClientTime(form.CapturedAt, now);
        var screenshot = new Screenshot
        {
            ScreenshotId = Guid.NewGuid(),
            SessionId = session.SessionId,
            ExamId = session.ExamId,
            Kind = kind,
            Trigger = trigger,
            TriggerReason = trigger == Screenshot.TriggerViolation ? Truncate(form.Reason, 64) : null,
            CapturedAt = capturedAt,
            UploadedAt = now,
            ContentType = contentType
        };

        StoredEvidence stored;
        try
        {
            stored = await _evidence.SaveAsync(session.ExamId, session.SessionId, screenshot.ScreenshotId, capturedAt, contentType, stream, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge,
                new ApiEnvelope<object>(false, "FILE_TOO_LARGE", "A capture may be at most 10 MB.", null));
        }
        screenshot.StoragePath = stored.RelativePath;
        screenshot.SizeBytes = stored.SizeBytes;
        screenshot.Sha256 = stored.Sha256;

        _db.Screenshots.Add(screenshot);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            _evidence.Delete(stored.RelativePath);   // no row → no file
            throw;
        }

        await _hubContext.Clients.Group(ExamGroupTracker.GroupName(session.ExamId)).SendAsync("ScreenshotUploaded", new
        {
            userId = studentId,
            sessionId = session.SessionId,
            examId = session.ExamId,
            screenshotId = screenshot.ScreenshotId,
            capturedAt = screenshot.CapturedAt,
            kind = screenshot.Kind,
            trigger = screenshot.Trigger,
            triggerReason = screenshot.TriggerReason
        }, cancellationToken);

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new
        {
            screenshotId = screenshot.ScreenshotId,
            capturedAt = screenshot.CapturedAt
        }));
    }

    // Client capture time (ISO 8601). Missing, unreadable, more than 2 min ahead or more than 24 h old → server time.
    private static DateTime ParseClientTime(string? value, DateTime now)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return now;
        parsed = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
        return parsed > now + MaxClientClockAhead || parsed < now - MaxClientEventAge ? now : parsed;
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
