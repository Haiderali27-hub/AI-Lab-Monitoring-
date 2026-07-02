using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Backend_API.Data;
using Microsoft.EntityFrameworkCore;
using Backend_API.Models.Enums;

namespace Backend_API.Hubs;

[Authorize]
public class MonitoringHub(AppDbContext db) : Hub
{
    private readonly AppDbContext _db = db;

    private bool IsStaff =>
        Context.User is not null &&
        (Context.User.IsInRole("Teacher") || Context.User.IsInRole("Admin") ||
         Context.User.IsInRole("SuperAdmin") || Context.User.IsInRole("Invigilator"));

    private Guid? CallerId
    {
        get
        {
            var sub = Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(sub, out var id) ? id : null;
        }
    }

    // Proctors join the group for the exam they're supervising; heartbeats/violations
    // are broadcast to this group only (students never receive other students' telemetry).
    public async Task JoinExamGroup(Guid examId)
    {
        if (!IsStaff)
            throw new HubException("Only staff can join exam monitoring groups.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"exam:{examId}");
    }

    public async Task LeaveExamGroup(Guid examId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"exam:{examId}");
    }

    // Legacy institution grouping kept for backward compatibility
    public async Task JoinInstitution(Guid institutionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"institution:{institutionId}");
    }

    public async Task LeaveInstitution(Guid institutionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"institution:{institutionId}");
    }

    public async Task SendWarning(string studentId, string message)
    {
        if (!IsStaff)
            throw new HubException("Only staff can send warnings.");

        if (Guid.TryParse(studentId, out var studentGuid))
        {
            var user = await _db.Users.FindAsync(studentGuid);
            if (user is not null)
            {
                _db.Notifications.Add(new Models.Notifications.Notification
                {
                    NotificationId = Guid.NewGuid(),
                    RecipientId = studentGuid,
                    Title = "Proctor Warning",
                    Body = message,
                    Type = "Warning",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }
        }
    }

    public async Task ForceSubmitSession(string studentId)
    {
        if (!IsStaff)
            throw new HubException("Only staff can force-submit sessions.");

        if (Guid.TryParse(studentId, out var studentGuid))
        {
            var session = await _db.ExamSessions
                .Include(s => s.Exam).ThenInclude(e => e.Section)
                .Where(s => s.UserId == studentGuid && s.Status == SessionStatus.InProgress)
                .FirstOrDefaultAsync();

            if (session is not null)
            {
                // Teachers may only act on sessions of exams in their own sections;
                // Admin/SuperAdmin can act on any.
                var isTeacherOnly = Context.User!.IsInRole("Teacher") &&
                                    !Context.User.IsInRole("Admin") && !Context.User.IsInRole("SuperAdmin");
                if (isTeacherOnly && session.Exam.Section.TeacherId != CallerId)
                    throw new HubException("You can only force-submit sessions for your own exams.");

                session.Status = SessionStatus.ForceSubmitted;
                session.SubmittedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
    }
}
