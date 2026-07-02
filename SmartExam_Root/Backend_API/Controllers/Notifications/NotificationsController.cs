using Backend_API.Data;
using Backend_API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Backend_API.Controllers.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public NotificationsController(AppDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    private Guid Me => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // GET /api/notifications
    // Returns logged-in user's notifications
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var notifications = await _db.Notifications
            .Where(n => n.RecipientId == Me)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.NotificationId, n.Title, n.Body,
                n.Type, n.IsRead, n.CreatedAt, n.RelatedEntityId
            })
            .ToListAsync();

        return Ok(new
        {
            UnreadCount = notifications.Count(n => !n.IsRead),
            Notifications = notifications
        });
    }

    // PATCH /api/notifications/{id}/read
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(n => n.NotificationId == id && n.RecipientId == Me);
        if (n is null) return NotFound();
        n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // PATCH /api/notifications/read-all
    // Marks all as read in one click
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var unread = await _db.Notifications
            .Where(n => n.RecipientId == Me && !n.IsRead)
            .ToListAsync();
        unread.ForEach(n => n.IsRead = true);
        await _db.SaveChangesAsync();
        return Ok(new { markedRead = unread.Count });
    }

    // POST /api/notifications/exam/{examId}/announce
    // Teacher sends a manual announcement to all students in an exam
    [HttpPost("exam/{examId:guid}/announce")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> AnnounceToExam(Guid examId, [FromBody] AnnounceRequest req)
    {
        var assignments = await _db.ExamAssignments
            .Where(a => a.ExamId == examId && a.IsEligible)
            .Select(a => a.UserId)
            .ToListAsync();

        if (!assignments.Any())
            return BadRequest(new { message = "No eligible students found for this exam." });

        await _notifications.SendToManyAsync(
            assignments,
            req.Title,
            req.Message,
            "Announcement",
            examId.ToString(),
            sendEmail: req.SendEmail
        );

        return Ok(new { message = $"Announcement sent to {assignments.Count} students." });
    }

    // POST /api/notifications/exam/{examId}/notify-scheduled
    // Called automatically after exam is created — notifies all assigned students
    [HttpPost("exam/{examId:guid}/notify-scheduled")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> NotifyScheduled(Guid examId)
    {
        await _notifications.NotifyExamScheduledAsync(examId);
        return Ok(new { message = "Students notified of exam schedule." });
    }

    // POST /api/notifications/exam/{examId}/notify-grades
    // Called after AI grading completes — notifies students results are ready
    [HttpPost("exam/{examId:guid}/notify-grades")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> NotifyGrades(Guid examId)
    {
        await _notifications.NotifyGradesReleasedAsync(examId);
        return Ok(new { message = "Students notified of grade release." });
    }
}

public record AnnounceRequest(string Title, string Message, bool SendEmail = false);
