using System.Security.Claims;
using System.Text.Json;
using Backend_API.Contracts;
using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

[ApiController]
[Route("api/monitoring")]
[Authorize(Roles = nameof(SystemRole.Student))]
public class MonitoringController(
    AppDbContext dbContext,
    IHubContext<MonitoringHub> hubContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IHubContext<MonitoringHub> _hubContext = hubContext;

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetClaims(out var studentId, out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Claims are missing or invalid."));
        }

        var now = DateTime.UtcNow;
        var payload = JsonSerializer.Serialize(new
        {
            request.IsForegroundExamApp,
            request.ActiveWindowTitle,
            request.ProcessListSnapshot
        });

        _dbContext.MonitoringEvents.Add(new MonitoringEvent
        {
            StudentUserId = studentId,
            ExamSessionId = request.ExamSessionId,
            EventType = "Heartbeat",
            PayloadJson = payload,
            CreatedAtUtc = now
        });

        var binding = await _dbContext.DeviceBindings.FirstOrDefaultAsync(
            x => x.StudentUserId == studentId,
            cancellationToken);
        if (binding is not null)
        {
            binding.LastSeenAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _hubContext.Clients.Group(MonitoringHub.InstitutionGroup(institutionId)).SendAsync(
            "monitoring_heartbeat",
            new
            {
                studentId,
                request.ExamSessionId,
                request.IsForegroundExamApp,
                request.ActiveWindowTitle,
                atUtc = now
            },
            cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { receivedAtUtc = now }));
    }

    [HttpPost("event")]
    public async Task<IActionResult> PushEvent([FromBody] MonitoringEventRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetClaims(out var studentId, out var institutionId))
        {
            return Unauthorized(ApiResponse<object>.Fail("UNAUTHORIZED", "Claims are missing or invalid."));
        }

        var now = DateTime.UtcNow;
        var eventEntity = new MonitoringEvent
        {
            StudentUserId = studentId,
            ExamSessionId = request.ExamSessionId,
            EventType = request.EventType,
            PayloadJson = request.PayloadJson,
            CreatedAtUtc = now
        };

        _dbContext.MonitoringEvents.Add(eventEntity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _hubContext.Clients.Group(MonitoringHub.InstitutionGroup(institutionId)).SendAsync(
            "monitoring_event",
            new
            {
                studentId,
                request.ExamSessionId,
                request.EventType,
                request.PayloadJson,
                atUtc = now
            },
            cancellationToken);

        return Ok(ApiResponse<object>.Ok(new { id = eventEntity.Id, atUtc = now }));
    }

    private bool TryGetClaims(out Guid studentId, out Guid institutionId)
    {
        studentId = Guid.Empty;
        institutionId = Guid.Empty;

        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var institutionIdClaim = User.FindFirstValue("institutionId");

        return Guid.TryParse(userIdClaim, out studentId) && Guid.TryParse(institutionIdClaim, out institutionId);
    }
}