using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M6 — Submission &amp; Backup, server side (TEST_CASES.md §M6, layer I).
/// M6-I-04 (force-submit-all grades) → SecurityHardeningTests.ForceSubmitAll_GradesTheForceSubmittedAnswers.
/// Contract: IMPLEMENTATION_PLAN.md §4.2 — save-answer revision (409 STALE_REVISION), submit receipt,
/// TimedOut sweeper (SessionTimeoutService), per-student ExtraMinutes (hub ExtendStudentTime).
/// </summary>
public class Module6SubmissionAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task M6_I_01_StaleRevision_Returns409_AndKeepsNewerAnswer()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var q = _host.Seed.Question2Id;

        (await TestHost.SaveAnswerAsync(student, sid, q, "rev 3 text", new { revision = 3, clientSavedAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stale = await TestHost.SaveAnswerAsync(student, sid, q, "rev 2 text", new { revision = 2, clientSavedAt = DateTime.UtcNow.AddSeconds(-10) });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TestHost.ErrorCodeAsync(stale)).Should().Be("STALE_REVISION");

        var stored = await _host.WithDbAsync(db => db.Answers.SingleAsync(a => a.SessionId == sid && a.QuestionId == q));
        stored.AnswerText.Should().Be("rev 3 text");
    }

    [Fact]
    public async Task M6_I_02_Submit_ReturnsReceipt_WithAnswerCountAndSha256()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question1Id, "int main(){return 0;}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "LIFO vs FIFO")).StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await TestHost.SubmitAsync(student, sid);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHost.DataAsync(resp);
        var receipt = body.TryGetProperty("receipt", out var r) ? r : body;

        receipt.GetProperty("submittedAt").GetDateTime().Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        receipt.GetProperty("answerCount").GetInt32().Should().Be(2);
        receipt.GetProperty("sha256").GetString().Should().MatchRegex("^[0-9a-fA-F]{64}$");

        // Teacher results show the same hash.
        var teacher = await _host.AsTeacher();
        var results = await (await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>();
        results!.Single(x => x.GetProperty("sessionId").GetGuid() == sid).GetProperty("receiptSha256").GetString()
            .Should().Be(receipt.GetProperty("sha256").GetString());
    }

    [Fact]
    public async Task M6_I_03_SessionStillInProgress2MinAfterEnd_BecomesTimedOut_AndIsGraded()
    {
        using var host = new TestHost(b => b.UseSetting("Submission:TimeoutSweepSeconds", "1"));
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, host.Seed.Question2Id, "stack is LIFO, queue is FIFO")).StatusCode.Should().Be(HttpStatusCode.OK);

        await host.EndExamWindowAsync(host.Seed.ExamId, minutesAgo: 3);   // end + 2 min grace already passed

        await TestHost.Eventually(
            () => host.WithDbAsync(async db => (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).Status == SessionStatus.TimedOut),
            TimeSpan.FromSeconds(15), "the sweeper must time out abandoned sessions");

        (await host.WithDbAsync(db => db.AiGradingResults.CountAsync(g => g.Answer.SessionId == sid))).Should().Be(1);
    }

    [Fact]
    public async Task M6_I_05_HubForceSubmitOneStudent_FinalizesAndGrades()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "A stack is LIFO; a queue is FIFO.")).StatusCode.Should().Be(HttpStatusCode.OK);

        var otherStudent = await _host.AsStudent2();
        var otherSid = await _host.StartSessionAsync(otherStudent, _host.Seed.ExamId);

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.ForceSubmitSession(_host.Seed.StudentId.ToString());

        var (status, grades, otherStatus) = await _host.WithDbAsync(async db => (
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).Status,
            await db.AiGradingResults.CountAsync(g => g.Answer.SessionId == sid),
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == otherSid)).Status));

        status.Should().Be(SessionStatus.ForceSubmitted);
        grades.Should().Be(1);
        otherStatus.Should().Be(SessionStatus.InProgress, "only the targeted student is force-submitted");

        // The student's client can no longer save.
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "after force")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task M6_I_06_LateSubmit_Returns200_LocksSession_AndAcceptsNoNewAnswers()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "on-time answer")).StatusCode.Should().Be(HttpStatusCode.OK);

        await _host.EndExamWindowAsync(_host.Seed.ExamId, minutesAgo: 10);

        // A save after the deadline is refused, even before submitting.
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question1Id, "late answer")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var submit = await TestHost.SubmitAsync(student, sid);
        submit.StatusCode.Should().Be(HttpStatusCode.OK);

        var (status, answers) = await _host.WithDbAsync(async db => (
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).Status,
            await db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerText).ToListAsync()));
        status.Should().Be(SessionStatus.Submitted);
        answers.Should().BeEquivalentTo(new[] { "on-time answer" });

        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question1Id, "after submit")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task M6_I_07_PerStudentExtraMinutes_AllowsSaveUntilEndPlusExtra_ForThatStudentOnly()
    {
        var s1 = await _host.AsStudent();
        var sid1 = await _host.StartSessionAsync(s1, _host.Seed.ExamId);
        var s2 = await _host.AsStudent2();
        var sid2 = await _host.StartSessionAsync(s2, _host.Seed.ExamId);

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await Planned.InvokeHubAsync(hub.Hub, "ExtendStudentTime", sid1, 10);

        // Exam ended 5 minutes ago (past the 2-minute grace) for everyone without extra time.
        await _host.EndExamWindowAsync(_host.Seed.ExamId, minutesAgo: 5);

        (await TestHost.SaveAnswerAsync(s1, sid1, _host.Seed.Question2Id, "still within my extra 10 min"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SaveAnswerAsync(s2, sid2, _host.Seed.Question2Id, "too late"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task M6_I_08_AnswersImmutableAfterSubmit_TeacherSeesOriginal()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "original submitted text")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "tampered")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question1Id, "new answer")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var teacher = await _host.AsTeacher();
        var results = await (await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>();
        var mine = results!.Single(r => r.GetProperty("sessionId").GetGuid() == sid);
        mine.GetProperty("status").GetString().Should().Be("Submitted");
        var answers = mine.GetProperty("answers").EnumerateArray().ToList();
        answers.Should().ContainSingle();
        answers[0].GetProperty("answerText").GetString().Should().Be("original submitted text");
    }

    [Fact]
    public async Task M6_I_09_RestartAfterClientCrash_StartSessionReturnsSameSession()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "before crash")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Client is killed and relaunched: new login, new HTTP client.
        var relaunched = await _host.AsStudent();
        var again = await _host.StartSessionAsync(relaunched, _host.Seed.ExamId);

        again.Should().Be(sid);
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.UserId == _host.Seed.StudentId))).Should().Be(1);
        (await TestHost.SaveAnswerAsync(relaunched, again, _host.Seed.Question1Id, "after relaunch")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.WithDbAsync(db => db.Answers.CountAsync(a => a.SessionId == sid))).Should().Be(2);
    }

    // ── Stage S4 additions (M6-I-10 …) ──────────────────────────────────────────

    private SessionTimeoutService Sweeper(TestHost host) =>
        host.Services.GetServices<IHostedService>().OfType<SessionTimeoutService>().Single();

    [Fact]
    public async Task M6_I_10_SameRevisionReplay_IsIdempotent_DifferentTextConflicts_AndUnrevisionedSaveStillWorks()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var q = _host.Seed.Question2Id;

        var first = await TestHost.SaveAnswerAsync(student, sid, q, "rev 1", new { revision = 1 });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.JsonAsync(first)).GetProperty("revision").GetInt32().Should().Be(1);

        // An offline queue replaying the same save after a lost ack: accepted, nothing changes.
        var replay = await TestHost.SaveAnswerAsync(student, sid, q, "rev 1", new { revision = 1 });
        replay.StatusCode.Should().Be(HttpStatusCode.OK);

        // Same revision, different text: two edits claimed the same number — never silently overwritten.
        var clash = await TestHost.SaveAnswerAsync(student, sid, q, "other rev 1", new { revision = 1 });
        clash.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var clashBody = await TestHost.JsonAsync(clash);
        clashBody.GetProperty("code").GetString().Should().Be("STALE_REVISION");
        clashBody.GetProperty("currentRevision").GetInt32().Should().Be(1);

        (await TestHost.SaveAnswerAsync(student, sid, q, "rev 2", new { revision = 2, clientSavedAt = DateTime.UtcNow }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.WithDbAsync(db => db.Answers.AsNoTracking().SingleAsync(a => a.SessionId == sid && a.QuestionId == q)))
            .ClientSavedAt.Should().NotBeNull("the client's own save time is kept for diagnostics");

        // Older clients (no revision) keep last-write-wins and leave the stored revision alone.
        (await TestHost.SaveAnswerAsync(student, sid, q, "legacy write")).StatusCode.Should().Be(HttpStatusCode.OK);
        var stored = await _host.WithDbAsync(db => db.Answers.AsNoTracking().SingleAsync(a => a.SessionId == sid && a.QuestionId == q));
        stored.AnswerText.Should().Be("legacy write");
        stored.Revision.Should().Be(2);

        (await TestHost.SaveAnswerAsync(student, sid, q, "negative", new { revision = -1 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task M6_I_11_SaveAnswer_ForQuestionOfAnotherExam_IsRejected()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var otherExam = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });
        var foreignQuestion = (await _host.QuestionIdsAsync(otherExam)).First();

        var resp = await TestHost.SaveAnswerAsync(student, sid, foreignQuestion, "planted");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _host.WithDbAsync(db => db.Answers.CountAsync(a => a.SessionId == sid))).Should().Be(0);

        var tooLong = await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, new string('x', 100_001));
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task M6_I_12_GetSessionAnswers_ReturnsOwnAnswersWithRevision_AndReceiptAfterSubmit_OthersGet404()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "stack LIFO", new { revision = 4 })).StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await student.GetAsync($"/api/exams/sessions/{sid}/answers");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHost.JsonAsync(resp);
        body.GetProperty("status").GetString().Should().Be("InProgress");
        body.GetProperty("remainingSeconds").GetInt32().Should().BeGreaterThan(0);
        var answer = body.GetProperty("answers").EnumerateArray().Single();
        answer.GetProperty("questionId").GetGuid().Should().Be(_host.Seed.Question2Id);
        answer.GetProperty("answerText").GetString().Should().Be("stack LIFO");
        answer.GetProperty("revision").GetInt32().Should().Be(4);
        body.GetProperty("receipt").ValueKind.Should().Be(JsonValueKind.Null);

        // Another student cannot read them (no enumeration: 404, as save-answer); staff have no access either.
        var other = await _host.AsStudent2();
        (await other.GetAsync($"/api/exams/sessions/{sid}/answers")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await _host.AsTeacher()).GetAsync($"/api/exams/sessions/{sid}/answers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var submit = await TestHost.JsonAsync(await TestHost.SubmitAsync(student, sid));
        var after = await TestHost.JsonAsync(await student.GetAsync($"/api/exams/sessions/{sid}/answers"));
        after.GetProperty("status").GetString().Should().Be("Submitted");
        after.GetProperty("receipt").GetProperty("sha256").GetString().Should().Be(submit.GetProperty("sha256").GetString());
        after.GetProperty("receipt").GetProperty("answerCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task M6_I_13_ReceiptHash_IsTheCanonicalHashOfTheStoredAnswers()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question1Id, "int main(){return 0;}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "A stack is LIFO; a queue is FIFO.")).StatusCode.Should().Be(HttpStatusCode.OK);

        var receipt = await TestHost.JsonAsync(await TestHost.SubmitAsync(student, sid));

        var expected = ReceiptHasher.Compute(sid, new (Guid, string?)[]
        {
            (_host.Seed.Question2Id, "A stack is LIFO; a queue is FIFO."),
            (_host.Seed.Question1Id, "int main(){return 0;}")
        });
        receipt.GetProperty("sha256").GetString().Should().Be(expected);
        receipt.GetProperty("sessionId").GetGuid().Should().Be(sid);

        var stored = await _host.WithDbAsync(db => db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid));
        stored.ReceiptSha256.Should().Be(expected);
        stored.ReceiptAnswerCount.Should().Be(2);

        // The live monitor is told (SessionFinalized → tile shows Submitted, M6-E-03).
        _host.Hub.Named("SessionFinalized").Should().ContainSingle(m =>
            m.Target == $"exam:{_host.Seed.ExamId}" && m.ArgsJson.Contains(sid.ToString()) && m.ArgsJson.Contains("Submitted"));
    }

    [Fact]
    public async Task M6_I_14_ExtendStudentTime_RequiresSupervision_ValidatesMinutes_IsAudited_AndPushed()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        using (var studentHub = _host.CreateHub(TestHost.Principal(_host.Seed.StudentId, "Student")))
            await ((Func<Task>)(() => studentHub.Hub.ExtendStudentTime(sid, 10))).Should().ThrowAsync<HubException>();

        var stranger = await _host.CreateUserAsync("Teacher");   // teaches no section of this exam
        using (var strangerHub = _host.CreateHub(TestHost.Principal(stranger.Id, "Teacher")))
            await ((Func<Task>)(() => strangerHub.Hub.ExtendStudentTime(sid, 10))).Should().ThrowAsync<HubException>();

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
        {
            await ((Func<Task>)(() => hub.Hub.ExtendStudentTime(sid, 0))).Should().ThrowAsync<HubException>();
            await ((Func<Task>)(() => hub.Hub.ExtendStudentTime(sid, 181))).Should().ThrowAsync<HubException>();
            await hub.Hub.ExtendStudentTime(sid, 10);
            await hub.Hub.ExtendStudentTime(sid, 5);
        }

        (await _host.WithDbAsync(db => db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid))).ExtraMinutes.Should().Be(15);
        var logs = await _host.AuditLogsAsync("SESSION_TIME_EXTENDED");
        logs.Should().HaveCount(2);
        logs.Select(l => l.GetRawText()).Should().OnlyContain(t => t.Contains(sid.ToString()));

        _host.Hub.Named("StudentTimeExtended").Should().Contain(m => m.Target == $"exam:{_host.Seed.ExamId}");
        _host.Hub.Named("StudentTimeExtended").Should().Contain(m => m.Target == $"user:{_host.Seed.StudentId}");

        // The student's own clock shows it (heartbeat ack and dashboard status), the exam's duration does not change.
        var ack = await TestHost.DataAsync(await TestHost.HeartbeatAsync(student, sid));
        ack.GetProperty("remainingSeconds").GetInt32().Should().BeGreaterThan(73 * 60);
        ack.GetProperty("extraMinutes").GetInt32().Should().Be(15);
        var current = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));
        current.GetProperty("remainingSeconds").GetInt32().Should().BeGreaterThan(73 * 60);

        // A finished session cannot be extended.
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await ((Func<Task>)(() => hub.Hub.ExtendStudentTime(sid, 5))).Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task M6_I_15_Sweeper_TimesOutOnlyOverdueSessions_RespectsExtraTime_AuditsAndBroadcasts()
    {
        var s1 = await _host.AsStudent();
        var sid1 = await _host.StartSessionAsync(s1, _host.Seed.ExamId);
        var s2 = await _host.AsStudent2();
        var sid2 = await _host.StartSessionAsync(s2, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(s2, sid2, _host.Seed.Question2Id, "stack and queue")).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.ExtendStudentTime(sid1, 10);

        // Still inside the window: nothing to do.
        (await Sweeper(_host).SweepAsync(DateTime.UtcNow)).Should().Be(0);

        await _host.EndExamWindowAsync(_host.Seed.ExamId, minutesAgo: 5);
        (await Sweeper(_host).SweepAsync(DateTime.UtcNow)).Should().Be(1);

        var (st1, st2, receipt2) = await _host.WithDbAsync(async db => (
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid1)).Status,
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid2)).Status,
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid2)).ReceiptSha256));
        st1.Should().Be(SessionStatus.InProgress, "student 1 still has 5 of their 10 extra minutes");
        st2.Should().Be(SessionStatus.TimedOut);
        receipt2.Should().MatchRegex("^[0-9a-f]{64}$");

        var logs = await _host.AuditLogsAsync("SESSION_TIMED_OUT");
        logs.Should().ContainSingle().Which.GetRawText().Should().Contain(sid2.ToString());
        _host.Hub.Named("SessionFinalized").Should().ContainSingle(m => m.ArgsJson.Contains(sid2.ToString()) && m.ArgsJson.Contains("TimedOut"));

        // A second sweep is a no-op (idempotent).
        (await Sweeper(_host).SweepAsync(DateTime.UtcNow)).Should().Be(0);

        // Once student 1's extra time (+ grace) has also passed, they are timed out too.
        (await Sweeper(_host).SweepAsync(DateTime.UtcNow.AddMinutes(8))).Should().Be(1);
        (await _host.WithDbAsync(db => db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid1))).Status
            .Should().Be(SessionStatus.TimedOut);
    }

    [Fact]
    public async Task M6_I_16_AfterTimeout_SaveSaysTimeIsOver_SubmitIsRefused_HeartbeatReportsStatus()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        await _host.EndExamWindowAsync(_host.Seed.ExamId, minutesAgo: 3);
        (await Sweeper(_host).SweepAsync(DateTime.UtcNow)).Should().Be(1);

        var late = await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "too late");
        late.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.JsonAsync(late)).GetProperty("message").GetString().Should().MatchRegex("(?i)time is over");

        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ack = await TestHost.DataAsync(await TestHost.HeartbeatAsync(student, sid));
        ack.GetProperty("sessionStatus").GetString().Should().Be("TimedOut");
        ack.GetProperty("remainingSeconds").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task M6_I_17_ResumeAfterExamEnd_AllowedOnlyWithinTheStudentsExtraTime()
    {
        var s1 = await _host.AsStudent();
        var sid1 = await _host.StartSessionAsync(s1, _host.Seed.ExamId);
        var s2 = await _host.AsStudent2();
        await _host.StartSessionAsync(s2, _host.Seed.ExamId);

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.ExtendStudentTime(sid1, 10);
        await _host.EndExamWindowAsync(_host.Seed.ExamId, minutesAgo: 5);

        // The desktop client crashed and relaunched: student/start returns the same running session.
        var relaunched = await _host.AsStudent();
        var resume = await relaunched.PostAsync("/api/exams/student/start", null);
        resume.StatusCode.Should().Be(HttpStatusCode.OK, await resume.Content.ReadAsStringAsync());
        (await TestHost.DataAsync(resume)).GetProperty("id").GetGuid().Should().Be(sid1);
        (await _host.StartSessionAsync(relaunched, _host.Seed.ExamId)).Should().Be(sid1);

        var status = await TestHost.DataAsync(await relaunched.GetAsync("/api/exams/student/current"));
        status.GetProperty("status").GetString().Should().Be("InProgress");
        status.GetProperty("remainingSeconds").GetInt32().Should().BeInRange(4 * 60, 5 * 60);

        // Without extra time the window is closed.
        var closed = await (await _host.AsStudent2()).PostAsync("/api/exams/student/start", null);
        closed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(closed)).Should().Be("EXAM_CLOSED");
    }

    [Fact]
    public async Task M6_I_18_ForceSubmitSessionById_FinalisesThatSessionOnly_AndIsAudited()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var other = await _host.AsStudent2();
        var otherSid = await _host.StartSessionAsync(other, _host.Seed.ExamId);

        var invigilator = await _host.CreateUserAsync("Invigilator");   // not assigned to this exam
        using (var hub = _host.CreateHub(TestHost.Principal(invigilator.Id, "Invigilator")))
            await ((Func<Task>)(() => hub.Hub.ForceSubmitSessionById(sid))).Should().ThrowAsync<HubException>();

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
        {
            await hub.Hub.ForceSubmitSessionById(sid);
            await ((Func<Task>)(() => hub.Hub.ForceSubmitSessionById(sid))).Should().ThrowAsync<HubException>("already submitted");
        }

        var (status, receipt, otherStatus) = await _host.WithDbAsync(async db => (
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).Status,
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).ReceiptSha256,
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == otherSid)).Status));
        status.Should().Be(SessionStatus.ForceSubmitted);
        receipt.Should().NotBeNullOrEmpty();
        otherStatus.Should().Be(SessionStatus.InProgress);
        (await _host.AuditLogsAsync("SESSION_FORCE_SUBMITTED")).Should().ContainSingle()
            .Which.GetRawText().Should().Contain(sid.ToString());

        // The student's client learns it from its next heartbeat and closes the exam window (D-17).
        (await TestHost.DataAsync(await TestHost.HeartbeatAsync(student, sid))).GetProperty("sessionStatus").GetString()
            .Should().Be("ForceSubmitted");
    }
}
