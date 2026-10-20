using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M6 — Submission &amp; Backup, server side (TEST_CASES.md §M6, layer I).
/// M6-I-04 (force-submit-all grades) → SecurityHardeningTests.ForceSubmitAll_GradesTheForceSubmittedAnswers.
/// Planned contract: IMPLEMENTATION_PLAN.md §4.2 — save-answer revision (409 STALE_REVISION), submit receipt,
/// TimedOut sweeper, per-student ExtraMinutes (hub ExtendStudentTime).
/// </summary>
public class Module6SubmissionAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact(Skip = "Pending: M6-I-01 — idempotent save with revision")]
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

    [Fact(Skip = "Pending: M6-I-02 — submit receipt")]
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

    [Fact(Skip = "Pending: M6-I-03 — TimedOut sweeper BackgroundService")]
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

    [Fact(Skip = "Pending: M6-I-07 — per-student extra time")]
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
}
