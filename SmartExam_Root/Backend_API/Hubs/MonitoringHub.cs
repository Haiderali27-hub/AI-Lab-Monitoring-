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
    // Longest single lockdown unlock (hub UnlockSession), and the text limits for its reason and for warnings.
    public const int MaxUnlockMinutes = 30;
    public const int MaxUnlockReasonLength = 500;
    public const int MaxWarningLength = 500;
    public const string WarningTitle = "Proctor Warning";
    public const string WarningType = "Warning";

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

    /// <summary>
    /// Module 10 (D-05) — a proctor's warning to one student. Only someone supervising an exam the student is sitting
    /// right now may send it. It is stored as a notification (so it is in the student's inbox and in the session
    /// timeline), pushed at once to every connection of that student (<c>ProctorWarning</c>), and returned in the
    /// desktop client's next heartbeat ack until the student acknowledges it — the WPF app has no SignalR client, so
    /// the ack is its delivery channel (within one heartbeat, 10 s). Audited PROCTOR_WARNING_SENT.
    /// </summary>
    public async Task SendWarning(string studentId, string message)
    {
        if (!IsStaff)
            throw new HubException("Only staff can send warnings.");
        if (!Guid.TryParse(studentId, out var studentGuid))
            throw new HubException("Unknown student.");
        var body = message?.Trim() ?? string.Empty;
        if (body.Length == 0)
            throw new HubException("Write the warning first.");
        if (body.Length > MaxWarningLength)
            throw new HubException($"A warning is limited to {MaxWarningLength} characters.");

        // Only someone supervising an exam the student is currently sitting may warn them.
        var sessions = await _db.ExamSessions.AsNoTracking()
            .Where(s => s.UserId == studentGuid && s.Status == SessionStatus.InProgress)
            .OrderByDescending(s => s.StartedAt)
            .Select(s => new { s.SessionId, s.ExamId })
            .ToListAsync();
        Guid? sessionId = null, examId = null;
        foreach (var s in sessions)
        {
            if (await _access.GetAsync(Context.User!, s.ExamId) == ExamAccess.None) continue;
            sessionId = s.SessionId;
            examId = s.ExamId;
            break;
        }
        if (sessionId is null || examId is null)
            throw new HubException("You can only warn students sitting an exam you supervise.");

        var notification = new Models.Notifications.Notification
        {
            NotificationId = Guid.NewGuid(),
            RecipientId = studentGuid,
            Title = WarningTitle,
            Body = body,
            Type = WarningType,
            IsRead = false,
            RelatedEntityId = sessionId.ToString(),
            CreatedAt = DateTime.UtcNow
        };
        _db.Notifications.Add(notification);
        _audit.Add(ExamAccessService.UserIdOf(Context.User!), "PROCTOR_WARNING_SENT", "ExamSession", sessionId.ToString(),
            new { examId, studentId = studentGuid, notificationId = notification.NotificationId, message = body });
        await _db.SaveChangesAsync();

        var payload = new
        {
            notificationId = notification.NotificationId,
            sessionId,
            examId,
            userId = studentGuid,
            title = notification.Title,
            body,
            createdAt = notification.CreatedAt
        };
        await Clients.User(studentGuid.ToString()).SendAsync("ProctorWarning", payload);
        // Every proctor of the exam sees it in the student's timeline, not only the sender.
        await Clients.Group(ExamGroupTracker.GroupName(examId.Value)).SendAsync("ProctorWarningSent", payload);
    }

    public async Task ForceSubmitSession(string studentId)
    {
        if (!IsStaff)
            throw new HubException("Only staff can force-submit sessions.");

        if (Guid.TryParse(studentId, out var studentGuid))
        {
            var sessions = await _db.ExamSessions
                .Include(s => s.Exam)
                .Where(s => s.UserId == studentGuid && s.Status == SessionStatus.InProgress)
                .OrderByDescending(s => s.StartedAt)
                .ToListAsync();
            if (sessions.Count == 0) return;

            // Teachers may only act on sessions of exams in their own sections, invigilators only on exams they are
            // assigned to; Admin/SuperAdmin on any. Only a session of an exam the caller supervises is ever chosen (the
            // student may also have a practice session open in an exam someone else supervises).
            foreach (var session in sessions)
            {
                if (await _access.GetAsync(Context.User!, session.ExamId) == ExamAccess.None) continue;
                await ForceSubmitAsync(session);
                return;
            }
            throw new HubException("You can only force-submit sessions for exams you supervise.");
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

    /// <summary>
    /// Module 10 — suspends one student's lockdown for <paramref name="minutes"/> (1..30), e.g. while an invigilator
    /// fixes the keyboard or has to open Settings on the PC. The reason is mandatory and audited (SESSION_UNLOCKED);
    /// a new unlock replaces the previous deadline. The student's client is told at once (<c>SessionUnlocked</c>) and
    /// also reads <c>unlockedUntilUtc</c> from every heartbeat ack, so it re-locks on time on its own even if the
    /// network drops in between. Monitoring (heartbeats, captures, violation reports) carries on while unlocked.
    /// </summary>
    public async Task UnlockSession(Guid sessionId, int minutes, string reason)
    {
        if (!IsStaff)
            throw new HubException("Only staff can unlock a student's PC.");
        if (minutes is < 1 or > MaxUnlockMinutes)
            throw new HubException($"An unlock lasts between 1 and {MaxUnlockMinutes} minutes.");
        var why = reason?.Trim() ?? string.Empty;
        if (why.Length == 0)
            throw new HubException("Give a reason for the unlock (it is recorded in the audit log).");
        if (why.Length > MaxUnlockReasonLength)
            throw new HubException($"The reason is limited to {MaxUnlockReasonLength} characters.");

        var session = await LoadRunningSessionAsync(sessionId, "unlock");
        session.UnlockedUntil = DateTime.UtcNow.AddMinutes(minutes);
        _audit.Add(ExamAccessService.UserIdOf(Context.User!), "SESSION_UNLOCKED", "ExamSession", session.SessionId.ToString(), new
        {
            examId = session.ExamId,
            studentId = session.UserId,
            minutes,
            reason = why,
            until = session.UnlockedUntil
        });
        await SaveSessionAsync();

        await NotifyUnlockAsync(session, "SessionUnlocked", new
        {
            examId = session.ExamId,
            sessionId = session.SessionId,
            userId = session.UserId,
            minutes,
            reason = why,
            unlockedUntilUtc = session.UnlockedUntil
        });
    }

    /// <summary>Ends an unlock early (the invigilator is done). Audited SESSION_RELOCKED; a no-op when not unlocked.</summary>
    public async Task RelockSession(Guid sessionId)
    {
        if (!IsStaff)
            throw new HubException("Only staff can lock a student's PC again.");

        var session = await LoadRunningSessionAsync(sessionId, "lock");
        if (session.UnlockedUntil is not DateTime until || until <= DateTime.UtcNow)
            return;

        session.UnlockedUntil = null;
        _audit.Add(ExamAccessService.UserIdOf(Context.User!), "SESSION_RELOCKED", "ExamSession", session.SessionId.ToString(),
            new { examId = session.ExamId, studentId = session.UserId, unlockedUntil = until });
        await SaveSessionAsync();

        await NotifyUnlockAsync(session, "SessionRelocked", new
        {
            examId = session.ExamId,
            sessionId = session.SessionId,
            userId = session.UserId
        });
    }

    // A running session of an exam the caller supervises (otherwise HubException).
    private async Task<Models.ExamSession> LoadRunningSessionAsync(Guid sessionId, string action)
    {
        var session = await _db.ExamSessions.Include(s => s.Exam).FirstOrDefaultAsync(s => s.SessionId == sessionId);
        if (session is null || await _access.GetAsync(Context.User!, session.ExamId) == ExamAccess.None)
            throw new HubException($"You can only {action} PCs in exams you supervise.");
        if (session.Status != SessionStatus.InProgress || session.Exam.Status == ExamStatus.Ended)
            throw new HubException("This session is no longer in progress.");
        return session;
    }

    private async Task SaveSessionAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new HubException("This session was submitted in the meantime.");
        }
    }

    // The student (fast path for any connected client) and every proctor watching the exam.
    private async Task NotifyUnlockAsync(Models.ExamSession session, string method, object payload)
    {
        await Clients.User(session.UserId.ToString()).SendAsync(method, payload);
        await Clients.Group(ExamGroupTracker.GroupName(session.ExamId)).SendAsync(method, payload);
    }
}
