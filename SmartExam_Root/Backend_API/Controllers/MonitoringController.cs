using System.Security.Claims;
using System.Text.Json;
using Backend_API.Data;
using Backend_API.DTOs;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
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
    IHubContext<MonitoringHub> hubContext) : ControllerBase
{
    private readonly AppDbContext _db = db;
    private readonly IHubContext<MonitoringHub> _hubContext = hubContext;

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
        int? remainingSeconds = null;

        if (session is not null)
        {
            answeredCount = await _db.Answers.CountAsync(a => a.SessionId == session.SessionId);
            totalQuestions = await _db.Questions.CountAsync(q => q.ExamId == session.ExamId);
            violationCount = await _db.MonitoringEvents.CountAsync(e => e.ExamSessionId == session.SessionId && e.EventType == MonitoringEventType.Violation);

            // Authoritative remaining time — lets the client resync its countdown
            // (e.g. after the teacher grants a time extension).
            var examEnd = session.Exam.StartTime.AddMinutes(session.Exam.DurationMinutes);
            remainingSeconds = Math.Max(0, (int)(examEnd - now).TotalSeconds);

            var assignment = await _db.ExamAssignments
                .Include(a => a.Workstation)
                .FirstOrDefaultAsync(a => a.ExamId == session.ExamId && a.UserId == studentId);
            if (assignment?.Workstation is not null)
            {
                workstationNumber = assignment.Workstation.MachineNumber;
            }
        }

        var tileStatus = violationCount > 0 ? "Violation" : "Normal";

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
                    answeredCount,
                    totalQuestions,
                    violationCount,
                    tileStatus
                });
        }

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new { receivedAtUtc = now, remainingSeconds }));
    }

    [HttpPost("event")]
    public async Task<IActionResult> PushEvent([FromBody] MonitoringEventPayload request)
    {
        var studentId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value!);
        var now = DateTime.UtcNow;

        var eventType = MonitoringEventType.WindowFocus;
        if (request.EventType == "Violation")
        {
            eventType = MonitoringEventType.Violation;
        }

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

            if (eventType == MonitoringEventType.Violation)
            {
                // Scoped to the proctors watching this exam (was Clients.All)
                await _hubContext.Clients.Group($"exam:{session.ExamId}").SendAsync(
                    "ViolationEvent",
                    new
                    {
                        userId = studentId,
                        eventType = request.EventType,
                        payload = request.PayloadJson
                    });
            }
        }
        // No session reference (e.g. pre-session attendance/identity check-in): accept as a
        // no-op so the client flow isn't blocked, but nothing is persisted.

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new { receivedAtUtc = now }));
    }
}
