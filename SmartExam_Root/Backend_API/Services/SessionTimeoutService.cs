using Backend_API.Data;
using Backend_API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

/// <summary>
/// Module 6 — server-side timer expiry. A student whose client crashed, lost power or never sent its
/// auto-submit would otherwise stay <c>InProgress</c> forever (no grade, no receipt, no plagiarism check).
/// Every <c>Submission:TimeoutSweepSeconds</c> (default 30; 0 or less disables the sweeper) this finds
/// sessions that are still in progress although their own deadline (exam end + per-student extra minutes
/// + <see cref="SubmissionService.DeadlineGrace"/>) has passed, or whose exam was ended, and finalises
/// them as <see cref="SessionStatus.TimedOut"/> through <see cref="SubmissionService.FinalizeAsync"/>
/// (so they are graded and get a receipt like any other submission), auditing SESSION_TIMED_OUT.
/// Answers already saved are kept; nothing the client sends after the deadline is accepted anyway.
/// </summary>
public sealed class SessionTimeoutService(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<SessionTimeoutService> logger) : BackgroundService
{
    public const string SweepSecondsKey = "Submission:TimeoutSweepSeconds";
    public const int DefaultSweepSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue(SweepSecondsKey, DefaultSweepSeconds);
        if (seconds <= 0)
        {
            logger.LogInformation("Session timeout sweeper disabled ({Key} = {Seconds}).", SweepSecondsKey, seconds);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
        do
        {
            try
            {
                await SweepAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed sweep (database briefly unreachable) must not stop future sweeps.
                logger.LogError(ex, "Session timeout sweep failed.");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Times out every overdue session once. Returns how many were finalised.</summary>
    public async Task<int> SweepAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        List<Guid> overdue;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Coarse filter in SQL (in-progress sessions of exams that have started: one lab's worth), the
            // exact per-student deadline (ExtraMinutes) in memory with the same rule save-answer uses.
            var candidates = await db.ExamSessions.AsNoTracking()
                .Include(s => s.Exam)
                .Where(s => s.Status == SessionStatus.InProgress && s.Exam.StartTime <= utcNow)
                .ToListAsync(cancellationToken);
            overdue = candidates.Where(s => SubmissionService.IsClosed(s, utcNow)).Select(s => s.SessionId).ToList();
        }

        var finalised = 0;
        foreach (var sessionId in overdue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One scope per session: a conflict on one (the student submitted in the same instant)
            // must not leave a dirty change tracker behind for the next.
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var submissions = scope.ServiceProvider.GetRequiredService<SubmissionService>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditService>();

            var session = await db.ExamSessions.Include(s => s.Exam)
                .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
            if (session is null || session.Status != SessionStatus.InProgress) continue;

            try
            {
                var effectiveEnd = SubmissionService.EffectiveEnd(session);
                audit.Add(null, "SESSION_TIMED_OUT", "ExamSession", session.SessionId.ToString(), new
                {
                    examId = session.ExamId,
                    studentId = session.UserId,
                    effectiveEndUtc = effectiveEnd,
                    extraMinutes = session.ExtraMinutes,
                    examEnded = session.Exam.Status == ExamStatus.Ended
                });
                var receipt = await submissions.FinalizeAsync(session, SessionStatus.TimedOut);
                finalised++;
                logger.LogInformation("Session {SessionId} timed out ({Answers} answers, receipt {Sha}).",
                    session.SessionId, receipt.AnswerCount, receipt.Sha256);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Finalised concurrently (student submit / proctor force-submit): nothing to do.
                logger.LogInformation("Session {SessionId} was finalised concurrently; timeout skipped.", sessionId);
            }
        }

        return finalised;
    }
}
