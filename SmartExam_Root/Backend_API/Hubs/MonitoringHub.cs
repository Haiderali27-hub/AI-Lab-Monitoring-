using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Backend_API.Data;
using Microsoft.EntityFrameworkCore;
using Backend_API.Models.Enums;
using Backend_API.Services;

namespace Backend_API.Hubs;

[Authorize]
public class MonitoringHub(AppDbContext db, SubmissionService submissions, ExamAccessService access,
    ExamGroupTracker tracker) : Hub
{
    private readonly AppDbContext _db = db;
    private readonly SubmissionService _submissions = submissions;
    private readonly ExamAccessService _access = access;
    private readonly ExamGroupTracker _tracker = tracker;

    private bool IsStaff =>
        Context.User is not null &&
        (Context.User.IsInRole("Teacher") || Context.User.IsInRole("Admin") ||
         Context.User.IsInRole("SuperAdmin") || Context.User.IsInRole("Invigilator"));

    // Proctors join the group for the exam they're supervising; heartbeats/violations
    // are broadcast to this group only (students never receive other students' telemetry).
    // The exam id comes from the client, so access is checked here: admins may join any exam,
    // a teacher the exams of their own sections, anyone else only exams they are assigned to
    // as a proctor (ExamProctors) — an invigilator cannot watch an exam by guessing its id.
    public async Task JoinExamGroup(Guid examId)
    {
        if (!IsStaff)
            throw new HubException("Only staff can join exam monitoring groups.");

        if (await _access.GetAsync(Context.User!, examId) == ExamAccess.None)
            throw new HubException("You are not assigned to supervise this exam.");

        // Tracked so withdrawing access later (unassign, deactivate, role change) also removes this
        // live connection from the group (ExamGroupTracker.RevokeAsync).
        var group = ExamGroupTracker.GroupName(examId);
        _tracker.Joined(Context.ConnectionId, ExamAccessService.UserIdOf(Context.User!)!.Value, examId);
        await Groups.AddToGroupAsync(Context.ConnectionId, group);

        // Re-check after joining: an unassignment that committed between the first check and the
        // registration above would otherwise have found nothing to revoke.
        if (await _access.GetAsync(Context.User!, examId) == ExamAccess.None)
        {
            _tracker.Left(Context.ConnectionId, examId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
            throw new HubException("You are not assigned to supervise this exam.");
        }
    }

    public async Task LeaveExamGroup(Guid examId)
    {
        _tracker.Left(Context.ConnectionId, examId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ExamGroupTracker.GroupName(examId));
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _tracker.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
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
        if (!Guid.TryParse(studentId, out var studentGuid))
            throw new HubException("Unknown student.");

        // Only someone supervising an exam the student is currently sitting may warn them.
        var examIds = await _db.ExamSessions.AsNoTracking()
            .Where(s => s.UserId == studentGuid && s.Status == SessionStatus.InProgress)
            .Select(s => s.ExamId)
            .Distinct()
            .ToListAsync();
        var allowed = false;
        foreach (var examId in examIds)
        {
            if (await _access.GetAsync(Context.User!, examId) != ExamAccess.None) { allowed = true; break; }
        }
        if (!allowed)
            throw new HubException("You can only warn students sitting an exam you supervise.");

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
                // Teachers may only act on sessions of exams in their own sections, invigilators only
                // on exams they are assigned to; Admin/SuperAdmin can act on any.
                if (await _access.GetAsync(Context.User!, session.ExamId) == ExamAccess.None)
                    throw new HubException("You can only force-submit sessions for exams you supervise.");

                await _submissions.FinalizeAsync(session, SessionStatus.ForceSubmitted);
            }
        }
    }
}
