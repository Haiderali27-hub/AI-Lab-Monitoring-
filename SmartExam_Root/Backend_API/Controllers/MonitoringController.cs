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

        _db.MonitoringEvents.Add(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            ExamSessionId = request.ExamSessionId ?? Guid.Empty,
            EventType = MonitoringEventType.Heartbeat,
            Payload = payload,
            RecordedAt = now
        });

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

        if (request.ExamSessionId.HasValue)
        {
            var session = await _db.ExamSessions
                .Include(s => s.Exam)
                .Include(s => s.Exam.Section)
                .FirstOrDefaultAsync(s => s.SessionId == request.ExamSessionId.Value);

            if (session is not null)
            {
                answeredCount = await _db.Answers.CountAsync(a => a.SessionId == request.ExamSessionId.Value);
                totalQuestions = await _db.Questions.CountAsync(q => q.ExamId == session.ExamId);
                violationCount = await _db.MonitoringEvents.CountAsync(e => e.ExamSessionId == request.ExamSessionId.Value && e.EventType == MonitoringEventType.Violation);
                
                var assignment = await _db.ExamAssignments
                    .Include(a => a.Workstation)
                    .FirstOrDefaultAsync(a => a.ExamId == session.ExamId && a.UserId == studentId);
                if (assignment?.Workstation is not null)
                {
                    workstationNumber = assignment.Workstation.MachineNumber;
                }
            }
        }

        var tileStatus = violationCount > 0 ? "Violation" : "Normal";

        // Broadcast to proctors
        await _hubContext.Clients.All.SendAsync(
            "StudentHeartbeat",
            new
            {
                userId = studentId,
                studentName = user?.Name ?? "Student",
                workstationNumber,
                sessionId = request.ExamSessionId,
                activeWindow = request.ActiveWindowTitle ?? "Unknown",
                answeredCount,
                totalQuestions,
                violationCount,
                tileStatus
            });

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new { receivedAtUtc = now }));
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

        _db.MonitoringEvents.Add(new MonitoringEvent
        {
            EventId = Guid.NewGuid(),
            ExamSessionId = request.ExamSessionId ?? Guid.Empty,
            EventType = eventType,
            Payload = request.PayloadJson,
            RecordedAt = now
        });

        await _db.SaveChangesAsync();

        if (eventType == MonitoringEventType.Violation)
        {
            await _hubContext.Clients.All.SendAsync(
                "ViolationEvent",
                new
                {
                    userId = studentId,
                    eventType = request.EventType,
                    payload = request.PayloadJson
                });
        }

        return Ok(new ApiEnvelope<object>(true, "SUCCESS", "Success", new { receivedAtUtc = now }));
    }
}
