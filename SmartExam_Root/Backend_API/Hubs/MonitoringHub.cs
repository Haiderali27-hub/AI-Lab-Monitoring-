using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Backend_API.Data;
using Microsoft.EntityFrameworkCore;
using Backend_API.Models.Enums;
using Backend_API.Services;

namespace Backend_API.Hubs;

[Authorize]
public class MonitoringHub(AppDbContext db, SubmissionService submissions, ExamAccessService access,
    ExamGroupTracker tracker, AuditService audit) : Hub
{
    // Upper bounds for per-student extra time: one grant, and all grants of a session together.
    public const int MaxExtraMinutesPerGrant = 180;
    public const int MaxExtraMinutesTotal = 600;

    private readonly AppDbContext _db = db;
    private readonly SubmissionService _submissions = submissions;
    private readonly ExamAccessService _access = access;
    private readonly ExamGroupTracker _tracker = tracker;
    private readonly AuditService _audit = audit;

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

                await ForceSubmitAsync(session);
            }
        }
    }

    /// <summary>
    /// Force-submits one session by id. Preferred over <see cref="ForceSubmitSession"/> (which picks the
    /// student's first in-progress session and is ambiguous when a practice session is also open, D-20).
    /// </summary>
    public async Task ForceSubmitSessionById(Guid sessionId)
    {
        if (!IsStaff)
            throw new HubException("Only staff can force-submit sessions.");

        var session = await _db.ExamSessions.Include(s => s.Exam).FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null || await _access.GetAsync(Context.User!, session.ExamId) == ExamAccess.None)
            throw new HubException("You can only force-submit sessions for exams you supervise.");
        if (session.Status != SessionStatus.InProgress)
            throw new HubException("This session has already been submitted.");

        await ForceSubmitAsync(session);
    }

    private async Task ForceSubmitAsync(Models.ExamSession session)
    {
        _audit.Add(ExamAccessService.UserIdOf(Context.User!), "SESSION_FORCE_SUBMITTED", "ExamSession", session.SessionId.ToString(),
            new { examId = session.ExamId, studentId = session.UserId });
        try
        {
            await _submissions.FinalizeAsync(session, SessionStatus.ForceSubmitted);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new HubException("This session was submitted in the meantime.");
        }
    }

    /// <summary>
    /// Module 6 / 10 — gives one student extra minutes (a crashed PC, a late start the student was not to
    /// blame for). Only that session's deadline moves: <c>ExamSession.ExtraMinutes</c> is added to the exam
    /// end by every server-side check (save-answer, heartbeat remainingSeconds, student/current, the timeout
    /// sweeper); the exam's duration and every other student are unchanged. Anyone supervising the exam may
    /// grant it (owner teacher, assigned invigilator, admin) — the whole exam can only be extended by its
    /// teacher or an admin (POST /api/exams/{id}/extend-time). Audited SESSION_TIME_EXTENDED. Ending the exam
    /// (force-submit-all) still closes the session.
    /// </summary>
    public async Task ExtendStudentTime(Guid sessionId, int minutes)
    {
        if (!IsStaff)
            throw new HubException("Only staff can extend a student's time.");
        if (minutes is < 1 or > MaxExtraMinutesPerGrant)
            throw new HubException($"Extra time must be between 1 and {MaxExtraMinutesPerGrant} minutes.");

        var session = await _db.ExamSessions.Include(s => s.Exam).FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null || await _access.GetAsync(Context.User!, session.ExamId) == ExamAccess.None)
            throw new HubException("You can only extend time for exams you supervise.");
        if (session.Status != SessionStatus.InProgress || session.Exam.Status == ExamStatus.Ended)
            throw new HubException("Only a session that is still in progress can be given extra time.");
        if (session.ExtraMinutes + minutes > MaxExtraMinutesTotal)
            throw new HubException($"A session can be given at most {MaxExtraMinutesTotal} extra minutes in total.");

        session.ExtraMinutes += minutes;
        var effectiveEnd = SubmissionService.EffectiveEnd(session);
        _audit.Add(ExamAccessService.UserIdOf(Context.User!), "SESSION_TIME_EXTENDED", "ExamSession", session.SessionId.ToString(), new
        {
            examId = session.ExamId,
            studentId = session.UserId,
            additionalMinutes = minutes,
            extraMinutes = session.ExtraMinutes,
            effectiveEndUtc = effectiveEnd
        });
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new HubException("This session was submitted in the meantime.");
        }

        var payload = new
        {
            examId = session.ExamId,
            sessionId = session.SessionId,
            userId = session.UserId,
            additionalMinutes = minutes,
            extraMinutes = session.ExtraMinutes,
            effectiveEndUtc = effectiveEnd,
            remainingSeconds = SubmissionService.RemainingSeconds(session, DateTime.UtcNow)
        };
        // Every proctor watching the exam, and the student (the desktop client also resyncs from the
        // heartbeat ack within one beat, so this push is only a fast path).
        await Clients.Group(ExamGroupTracker.GroupName(session.ExamId)).SendAsync("StudentTimeExtended", payload);
        await Clients.User(session.UserId.ToString()).SendAsync("StudentTimeExtended", payload);
    }
}
