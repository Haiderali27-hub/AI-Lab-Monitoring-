using System.Security.Claims;
using System.Text.Json;
using Backend_API.Data;
using Backend_API.DTOs;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
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
    SeatGuard seats) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly IHubContext<MonitoringHub> _hubContext = hubContext;
    private readonly SeatGuard _seats = seats;

    // A student sitting at the wrong seat heartbeats every 10 s; record the violation once per
    // (session, seat actually used) in this window instead of on every beat.
    public static readonly TimeSpan SeatMismatchDedupWindow = TimeSpan.FromMinutes(5);

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatPayload request)
    {
        var studentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
        var now = DateTime.UtcNow;

        var payload = JsonSerializer.Serialize(new
        {
            request.IsForegroundExamApp,
            request.ActiveWindowTitle,
            request.ProcessListSnapshot
        });

        // Only persist a MonitoringEvent when it can be attributed to a real exam
        // session; otherwise the ExamSessionId FK would be violated (heartbeats can
        // legitimately arrive before/without an active session — those still update
        // liveness, just aren't stored as session events).
        // Ownership-scoped: only accept a session that belongs to the caller (prevents a
        // student from writing telemetry into another student's session).
        var session = request.ExamSessionId.HasValue && request.ExamSessionId.Value != Guid.Empty
            ? await _db.ExamSessions
                .Include(s => s.Exam)
                .FirstOrDefaultAsync(s => s.SessionId == request.ExamSessionId.Value && s.UserId == studentId)
            : null;

        if (session is not null)
        {
            _db.MonitoringEvents.Add(new MonitoringEvent
            {
                EventId = Guid.NewGuid(),
                ExamSessionId = session.SessionId,
                EventType = MonitoringEventType.Heartbeat,
                Payload = payload,
                RecordedAt = now
            });
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
                            RecordedAt = now
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

        await _db.SaveChangesAsync();

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

            // Authoritative remaining time — lets the client resync its countdown
            // (e.g. after the teacher grants a time extension).
            var examEnd = session.Exam.StartTime.AddMinutes(session.Exam.DurationMinutes);
            remainingSeconds = Math.Max(0, (int)(examEnd - now).TotalSeconds);

            if (assignment?.Workstation is not null)
            {
                workstationNumber = assignment.Workstation.MachineNumber;
            }
        }

        var tileStatus = ViolationClassifier.Classify(violationTypes);

        // Broadcast to the proctors watching THIS exam only (was Clients.All)
        if (session is not null)
        {
            await _hubContext.Clients.Group($"exam:{session.ExamId}").SendAsync(
                "StudentHeartbeat",
                new
                {
                    userId = studentId,
                    studentName = user?.Name ?? "Student",
                    workstationNumber,
                    machineName = user?.DeviceBinding?.MachineName,          // real Windows PC name
                    windowsUsername = user?.DeviceBinding?.WindowsUsername,  // Windows account in use
                    sessionId = request.ExamSessionId,
                    activeWindow = request.ActiveWindowTitle ?? "Unknown",
                    isForegroundExamApp = request.IsForegroundExamApp,
                    answeredCount,
                    totalQuestions,
                    violationCount,
                    tileStatus,
                    // Module 9: Ok | Mismatch | Unassigned | NotEnforced | NotChecked
                    seatStatus = seat?.Status.ToString() ?? nameof(SeatStatus.Unassigned),
                    expectedSeat = seat?.ExpectedSeat,
                    actualMachine = seat?.ActualMachine,
                    seatDetail = seat is { IsMismatch: true } ? seat.Detail : null
                });
        }

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new { receivedAtUtc = now, remainingSeconds }));
    }

    [HttpPost("event")]
    public async Task<IActionResult> PushEvent([FromBody] MonitoringEventPayload request)
    {
        var studentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
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
                RecordedAt = now
            });
            await _db.SaveChangesAsync();

            if (ViolationClassifier.IsViolation(eventType))
            {
                // Scoped to the proctors watching this exam (was Clients.All)
                await _hubContext.Clients.Group($"exam:{session.ExamId}").SendAsync(
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
}
