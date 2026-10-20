using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// Cross-module authorization regressions (S8 verifier pass):
///  • D-39 — every endpoint that acts on one exam (or on a session / answer of it) is the section teacher's or an
///    admin's; an assigned proctor (ExamProctors) gets only the read / proctoring ones; anyone else 403, an unknown id
///    404, and a refused call changes and audits nothing. The monitoring hub applies the same rule.
///  • D-06 — heartbeat / event / batch / screenshot naming another student's (or an unknown) session: 404, nothing kept.
///  • Device-binding reset signs the student out everywhere (refresh tokens + access tokens).
/// </summary>
public class OwnershipAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private static byte[] Jpeg()
    {
        var head = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 };
        return head.Concat(new byte[256]).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient c, Guid sessionId)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(sessionId.ToString()), "examSessionId");
        form.Add(new StringContent("Screen"), "kind");
        var file = new ByteArrayContent(Jpeg());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "screen.jpg");
        return c.PostAsync("/api/monitoring/screenshot", form);
    }

    private static object BatchFor(Guid sessionId) => new
    {
        examSessionId = sessionId,
        events = new[] { new { clientEventId = Guid.NewGuid(), eventType = "FocusLost", payloadJson = "{\"framed\":true}", recordedAt = DateTime.UtcNow } }
    };

    private object ExamPayload() => new
    {
        title = "Hijacked",
        instructions = "x",
        sectionId = _host.Seed.SectionId,
        startTime = DateTime.UtcNow.AddDays(2),
        durationMinutes = 600,
        allowedApps = "[\"code.exe\"]",
        aiEvaluationEnabled = false,
        plagiarismThreshold = 70,
        questions = new[] { TestHost.TheoryQuestion() },
        studentIds = Array.Empty<Guid>()
    };

    /// <summary>Every per-exam staff request, for exam <paramref name="examId"/> (session <paramref name="sid"/>).</summary>
    private IEnumerable<(string Name, Func<HttpClient, Task<HttpResponseMessage>> Call)> OwnerOnlyCalls(Guid examId, Guid sid)
    {
        var student = _host.Seed.StudentId;
        yield return ("PUT exam", c => c.PutAsJsonAsync($"/api/exams/{examId}", ExamPayload()));
        yield return ("DELETE exam", c => c.DeleteAsync($"/api/exams/{examId}"));
        yield return ("GET proctors", c => c.GetAsync($"/api/exams/{examId}/proctors"));
        yield return ("POST proctors", c => c.PostAsJsonAsync($"/api/exams/{examId}/proctors", new { userId = _host.Seed.AdminId }));
        yield return ("DELETE proctor", c => c.DeleteAsync($"/api/exams/{examId}/proctors/{_host.Seed.AdminId}"));
        yield return ("GET assignments", c => c.GetAsync($"/api/exams/{examId}/assignments"));
        yield return ("PUT eligibility", c => c.PutAsJsonAsync($"/api/exams/{examId}/eligibility",
            new { assignments = new[] { new { userId = student, isEligible = false, eligibilityNote = "hijacked" } } }));
        yield return ("POST recalculate", c => c.PostAsync($"/api/exams/{examId}/eligibility/recalculate", null));
        yield return ("POST assignment", c => c.PostAsJsonAsync($"/api/exams/{examId}/assignments", new { userId = student }));
        yield return ("DELETE assignment", c => c.DeleteAsync($"/api/exams/{examId}/assignments/{_host.Seed.Student2Id}"));
        yield return ("PUT workstation", c => c.PutAsJsonAsync($"/api/exams/{examId}/assignments/{student}/workstation", new { workstationId = (Guid?)null }));
        yield return ("POST extend-time", c => c.PostAsJsonAsync($"/api/exams/{examId}/extend-time", new { additionalMinutes = 30 }));
        yield return ("POST force-submit-all", c => c.PostAsync($"/api/exams/{examId}/force-submit-all", null));
        yield return ("GET results", c => c.GetAsync($"/api/exams/{examId}/results"));
        yield return ("GET plagiarism", c => c.GetAsync($"/api/exams/{examId}/plagiarism"));
        yield return ("GET plagiarism matrix", c => c.GetAsync($"/api/exams/{examId}/plagiarism/matrix"));
        yield return ("POST accept-ai", c => c.PostAsync($"/api/exams/{examId}/grading/accept-ai", null));
        yield return ("GET analytics summary", c => c.GetAsync($"/api/analytics/exams/{examId}/summary"));
        yield return ("POST analytics report", c => c.PostAsync($"/api/analytics/exams/{examId}/report", null));
        yield return ("GET analytics reports", c => c.GetAsync($"/api/analytics/exams/{examId}/reports"));
        yield return ("POST announce", c => c.PostAsJsonAsync($"/api/notifications/exam/{examId}/announce",
            new { title = "Fake", message = "Exam cancelled", sendEmail = false }));
        yield return ("POST notify-scheduled", c => c.PostAsync($"/api/notifications/exam/{examId}/notify-scheduled", null));
        yield return ("POST notify-grades", c => c.PostAsync($"/api/notifications/exam/{examId}/notify-grades", null));
    }

    /// <summary>Read / proctoring requests an assigned proctor may make (owner and admin too).</summary>
    private static IEnumerable<(string Name, Func<HttpClient, Task<HttpResponseMessage>> Call)> SupervisorCalls(Guid examId, Guid sid)
    {
        yield return ("GET exam", c => c.GetAsync($"/api/exams/{examId}"));
        yield return ("GET readiness", c => c.GetAsync($"/api/exams/{examId}/readiness"));
        yield return ("GET live", c => c.GetAsync($"/api/exams/{examId}/live"));
        yield return ("GET incidents", c => c.GetAsync($"/api/exams/{examId}/incidents"));
        yield return ("GET screenshots", c => c.GetAsync($"/api/exams/{examId}/sessions/{sid}/screenshots"));
        yield return ("GET timeline", c => c.GetAsync($"/api/exams/{examId}/sessions/{sid}/timeline"));
        yield return ("GET processes", c => c.GetAsync($"/api/exams/{examId}/sessions/{sid}/processes"));
    }

    private async Task<(Guid SessionId, Guid AnswerId)> SubmittedAnswerAsync()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "A stack is LIFO, a queue is FIFO."))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        var answerId = await _host.WithDbAsync(db => db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerId).SingleAsync());
        return (sid, answerId);
    }

    // What the notification endpoints would have sent (announce / notify-scheduled / notify-grades).
    private static readonly string[] StudentMessageTypes = ["Announcement", "ExamScheduled", "GradeReleased"];

    private Task<int> AuditRowsByAsync(Guid actorId) =>
        _host.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.ActorId == actorId && a.EventType != "LOGIN_SUCCESS"));

    [Fact]
    public async Task M10_I_18_PerExamEndpoints_UnrelatedTeacher403_UnknownId404_NothingChangedOrAudited()
    {
        // D-39: results, plagiarism report + matrix, override / bulk accept / regrade, exam GET/PUT/DELETE, roster,
        // extend-time, force-submit-all, analytics + PDF reports, notifications, live snapshot, timeline, screenshots,
        // incidents, readiness — all refused to another section's teacher, 404 for an id that does not exist.
        var (sid, answerId) = await SubmittedAnswerAsync();
        var examId = _host.Seed.ExamId;
        var owner = await _host.AsTeacher();
        var gen = await owner.PostAsync($"/api/analytics/exams/{examId}/report", null);
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var downloadUrl = (await TestHost.JsonAsync(gen)).GetProperty("downloadUrl").GetString()!;
        var reportsBefore = await _host.WithDbAsync(db => db.ExamReports.CountAsync());
        var notificationsBefore = await _host.WithDbAsync(db => db.Notifications.CountAsync(n => StudentMessageTypes.Contains(n.Type)));

        var (outsiderId, email, password) = await _host.CreateUserAsync("Teacher");
        var outsider = await _host.LoginAsync(email, password);

        var calls = OwnerOnlyCalls(examId, sid).Concat(SupervisorCalls(examId, sid)).ToList();
        calls.Add(("GET exam detail", c => c.GetAsync($"/api/exams/{examId}")));
        calls.Add(("POST override", c => c.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 10, note = "hijacked" })));
        calls.Add(("POST regrade", c => c.PostAsync($"/api/answers/{answerId}/regrade", null)));
        calls.Add(("GET report file", c => c.GetAsync(downloadUrl)));
        calls.Add(("GET section students", c => c.GetAsync($"/api/exams/sections/{_host.Seed.SectionId}/students")));
        calls.Add(("POST incident", c => c.PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Low", notes = "x" })));
        foreach (var (name, call) in calls)
            (await call(outsider)).StatusCode.Should().Be(HttpStatusCode.Forbidden, name);

        // Unknown ids: 404, never 403 or 500.
        var unknown = Guid.NewGuid();
        var unknownCalls = OwnerOnlyCalls(unknown, sid).Concat(SupervisorCalls(unknown, sid)).ToList();
        unknownCalls.Add(("POST override", c => c.PostAsJsonAsync($"/api/answers/{unknown}/override", new { finalMarks = 1 })));
        unknownCalls.Add(("POST regrade", c => c.PostAsync($"/api/answers/{unknown}/regrade", null)));
        unknownCalls.Add(("GET report file", c => c.GetAsync($"/api/analytics/reports/Exam_Report_{unknown}_20260101_000000.pdf")));
        unknownCalls.Add(("GET section students", c => c.GetAsync($"/api/exams/sections/{unknown}/students")));
        unknownCalls.Add(("POST incident", c => c.PostAsJsonAsync("/api/incidents", new { sessionId = unknown, severity = "Low", notes = "x" })));
        foreach (var (name, call) in unknownCalls)
            (await call(outsider)).StatusCode.Should().Be(HttpStatusCode.NotFound, "unknown id: " + name);

        // Nothing changed and nothing was recorded on the outsider's behalf.
        await _host.WithDbAsync(async db =>
        {
            var exam = await db.Exams.AsNoTracking().SingleAsync(e => e.ExamId == examId);
            exam.DurationMinutes.Should().Be(60);
            exam.Title.Should().Be("Test Exam");
            exam.Status.Should().NotBe(ExamStatus.Ended);
            (await db.ExamAssignments.CountAsync(a => a.ExamId == examId)).Should().Be(2);
            (await db.ExamAssignments.AnyAsync(a => a.ExamId == examId && !a.IsEligible)).Should().BeFalse();
            (await db.ExamProctors.AnyAsync(p => p.ExamId == examId)).Should().BeFalse();
            (await db.TeacherGradeOverrides.AnyAsync()).Should().BeFalse();
            (await db.Incidents.AnyAsync()).Should().BeFalse();
            (await db.ExamReports.CountAsync()).Should().Be(reportsBefore);
            (await db.Notifications.CountAsync(n => StudentMessageTypes.Contains(n.Type))).Should().Be(notificationsBefore);
        });
        (await AuditRowsByAsync(outsiderId)).Should().Be(0);

        // The owner (and an admin) still get every one of them.
        foreach (var (name, call) in SupervisorCalls(examId, sid))
            (await call(owner)).StatusCode.Should().Be(HttpStatusCode.OK, "owner: " + name);
        var admin = await _host.AsAdmin();
        foreach (var url in new[] { $"/api/exams/{examId}/results", $"/api/analytics/exams/{examId}/summary", downloadUrl,
                     $"/api/exams/sections/{_host.Seed.SectionId}/students" })
            (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, "admin: " + url);
        (await owner.GetAsync(downloadUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 7, note = "ok" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M10_I_19_AssignedProctor_MayWatchAndSupervise_ButNotManageOrGrade()
    {
        // A co-proctoring teacher passes the Teacher role gate everywhere, so only ExamAccessService stops them.
        var (sid, answerId) = await SubmittedAnswerAsync();
        var examId = _host.Seed.ExamId;
        var (coId, coEmail, coPassword) = await _host.CreateUserAsync("Teacher");
        (await (await _host.AsTeacher()).PostAsJsonAsync($"/api/exams/{examId}/proctors", new { userId = coId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var coProctor = await _host.LoginAsync(coEmail, coPassword);

        foreach (var (name, call) in SupervisorCalls(examId, sid))
            (await call(coProctor)).StatusCode.Should().Be(HttpStatusCode.OK, "proctor may: " + name);
        (await coProctor.PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Low", notes = "Talking" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        foreach (var (name, call) in OwnerOnlyCalls(examId, sid))
            (await call(coProctor)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "proctor may not: " + name);
        (await coProctor.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 10 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await coProctor.PostAsync($"/api/answers/{answerId}/regrade", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // An assigned invigilator: the same read endpoints, and the proctor view of the exam (no answer key).
        var (invId, invEmail, invPassword) = await _host.CreateUserAsync("Invigilator");
        (await (await _host.AsTeacher()).PostAsJsonAsync($"/api/exams/{examId}/proctors", new { userId = invId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var invigilator = await _host.LoginAsync(invEmail, invPassword);
        foreach (var (name, call) in SupervisorCalls(examId, sid))
            (await call(invigilator)).StatusCode.Should().Be(HttpStatusCode.OK, "invigilator may: " + name);
        var detail = await TestHost.JsonAsync(await invigilator.GetAsync($"/api/exams/{examId}"));
        detail.GetProperty("questions").EnumerateArray().Should().OnlyContain(q => q.GetProperty("referenceAnswer").ValueKind == JsonValueKind.Null);

        await _host.WithDbAsync(async db =>
        {
            (await db.TeacherGradeOverrides.AnyAsync()).Should().BeFalse();
            (await db.Exams.AsNoTracking().SingleAsync(e => e.ExamId == examId)).DurationMinutes.Should().Be(60);
        });
        (await _host.WithDbAsync(db => db.AuditLogs.CountAsync(a => a.ActorId == coId && a.EventType != "LOGIN_SUCCESS" && a.EventType != "INCIDENT_CREATED")))
            .Should().Be(0);
    }

    [Fact]
    public async Task M10_I_20_HubMethods_RefuseNonSupervisors_AndChangeNothing()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var (outsiderId, _, _) = await _host.CreateUserAsync("Teacher");
        var (strangerId, _, _) = await _host.CreateUserAsync("Invigilator");

        foreach (var principal in new[] { TestHost.Principal(outsiderId, "Teacher"), TestHost.Principal(strangerId, "Invigilator") })
        {
            using var hub = _host.CreateHub(principal);
            var calls = new (string Name, Func<Task> Call)[]
            {
                ("JoinExamGroup", () => hub.Hub.JoinExamGroup(_host.Seed.ExamId)),
                ("SendWarning", () => hub.Hub.SendWarning(_host.Seed.StudentId.ToString(), "Eyes on your screen")),
                ("ExtendStudentTime", () => hub.Hub.ExtendStudentTime(sid, 30)),
                ("ForceSubmitSessionById", () => hub.Hub.ForceSubmitSessionById(sid)),
                ("ForceSubmitSession", () => hub.Hub.ForceSubmitSession(_host.Seed.StudentId.ToString())),
                ("UnlockSession", () => hub.Hub.UnlockSession(sid, 5, "Keyboard broken")),
                ("RelockSession", () => hub.Hub.RelockSession(sid)),
            };
            foreach (var (name, call) in calls)
                await call.Should().ThrowAsync<HubException>(name);
            hub.Groups.Added.Should().BeEmpty();
            // Unknown session / exam: refused the same way.
            await hub.Hub.Awaiting(h => h.ExtendStudentTime(Guid.NewGuid(), 5)).Should().ThrowAsync<HubException>();
            await hub.Hub.Awaiting(h => h.JoinExamGroup(Guid.NewGuid())).Should().ThrowAsync<HubException>();
        }

        await _host.WithDbAsync(async db =>
        {
            var s = await db.ExamSessions.AsNoTracking().SingleAsync(x => x.SessionId == sid);
            s.Status.Should().Be(SessionStatus.InProgress);
            s.ExtraMinutes.Should().Be(0);
            s.UnlockedUntil.Should().BeNull();
            (await db.Notifications.AnyAsync(n => n.Type == "Warning")).Should().BeFalse();
            (await db.AuditLogs.AnyAsync(a => a.ActorId == outsiderId || a.ActorId == strangerId)).Should().BeFalse();
        });
        foreach (var method in new[] { "ProctorWarning", "ProctorWarningSent", "StudentTimeExtended", "SessionUnlocked", "SessionRelocked" })
            _host.Hub.Named(method).Should().BeEmpty("nothing was broadcast: " + method);

        // Assigned through ExamProctors, the same invigilator may supervise this student.
        (await (await _host.AsTeacher()).PostAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/proctors", new { userId = strangerId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        using (var hub = _host.CreateHub(TestHost.Principal(strangerId, "Invigilator")))
        {
            await hub.Hub.JoinExamGroup(_host.Seed.ExamId);
            await hub.Hub.SendWarning(_host.Seed.StudentId.ToString(), "Eyes on your screen");
            await hub.Hub.ExtendStudentTime(sid, 5);
            await hub.Hub.UnlockSession(sid, 5, "Keyboard broken");
            await hub.Hub.RelockSession(sid);
            await hub.Hub.ForceSubmitSessionById(sid);
        }
        var final = await _host.WithDbAsync(db => db.ExamSessions.AsNoTracking().SingleAsync(x => x.SessionId == sid));
        final.Status.Should().Be(SessionStatus.ForceSubmitted);
        final.ExtraMinutes.Should().Be(5);
    }

    [Fact]
    public async Task M5_I_22_ForeignOrUnknownSession_IsRefusedOnEveryClientEndpoint_AndNothingIsRecorded()
    {
        // D-06: a heartbeat naming another student's session answered 200 (ignored). Every student endpoint that names
        // a session now answers 404 for a session that is not the caller's — unknown or foreign alike — and keeps nothing.
        var victim = await _host.AsStudent();
        var victimSession = await _host.StartSessionAsync(victim, _host.Seed.ExamId);
        var attacker = await _host.StudentClientLoginAsync(SeededData.Student2Email, SeededData.Student2Password, SeededData.TestHwid2, "LAB-PC-09");
        var attackerSeenBefore = await _host.WithDbAsync(db => db.DeviceBindings.Where(d => d.UserId == _host.Seed.Student2Id)
            .Select(d => d.LastSeenAt).SingleAsync());

        foreach (var target in new[] { victimSession, Guid.NewGuid() })
        {
            var heartbeat = await TestHost.HeartbeatAsync(attacker, target);
            heartbeat.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await TestHost.ErrorCodeAsync(heartbeat)).Should().Be("NOT_FOUND");
            (await TestHost.PostEventAsync(attacker, target, "Violation", "{\"reason\":\"FocusLoss\"}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await attacker.PostAsJsonAsync("/api/monitoring/events/batch", BatchFor(target))).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await UploadAsync(attacker, target)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        await _host.WithDbAsync(async db =>
        {
            (await db.MonitoringEvents.AnyAsync(e => e.ExamSessionId == victimSession)).Should().BeFalse();
            (await db.Screenshots.AnyAsync()).Should().BeFalse();
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == victimSession)).LastHeartbeatAt.Should().BeNull();
            (await db.DeviceBindings.Where(d => d.UserId == _host.Seed.Student2Id).Select(d => d.LastSeenAt).SingleAsync())
                .Should().Be(attackerSeenBefore, "a refused heartbeat does not count as the attacker's PC being seen");
        });
        _host.Hub.Named("StudentHeartbeat").Should().BeEmpty();
        _host.Hub.Named("ViolationEvent").Should().BeEmpty();
        _host.Hub.Named("ScreenshotUploaded").Should().BeEmpty();

        // A heartbeat without a session (before the exam starts) and the victim's own heartbeat still work.
        (await attacker.PostAsJsonAsync("/api/monitoring/heartbeat", new { isForegroundExamApp = true, activeWindowTitle = "SmartExam" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.HeartbeatAsync(victim, victimSession)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M1_I_14_DeviceBindingReset_RevokesEveryRefreshTokenAndAccessToken()
    {
        // The desktop session on the old PC (and any web session) must not outlive the binding that admitted it.
        var c = _host.NewClient();
        var login = await _host.StudentLoginRawAsync(c, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, "OLD-PC");
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await TestHost.JsonAsync(login)).GetProperty("data");
        var refreshToken = data.GetProperty("refreshToken").GetString()!;
        AuthHelper.SetToken(c, data.GetProperty("accessToken").GetString()!);
        var web = await _host.AsStudent();
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await web.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var admin = await _host.AsAdmin();
        var reset = await admin.DeleteAsync($"/api/users/{_host.Seed.StudentId}/device-binding");
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHost.JsonAsync(reset);
        body.GetProperty("revokedRefreshTokens").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("revokedSessions").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        // Both access tokens are dead at once, and the refresh token cannot mint a new one.
        (await c.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await web.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.NewClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _host.WithDbAsync(async db =>
        {
            (await db.RefreshTokens.Where(t => t.UserId == _host.Seed.StudentId).ToListAsync())
                .Should().OnlyContain(t => t.RevokedAt != null);
            (await db.RefreshTokens.AnyAsync(t => t.UserId == _host.Seed.StudentId && t.RevokedReason == RefreshTokenRevocation.DeviceReset))
                .Should().BeTrue();
            (await db.UserSessions.AnyAsync(s => s.UserId == _host.Seed.StudentId && !s.IsRevoked)).Should().BeFalse();
            (await db.DeviceBindings.AnyAsync(d => d.UserId == _host.Seed.StudentId)).Should().BeFalse();
        });
        var audit = (await _host.AuditLogsAsync("DEVICE_BINDING_RESET")).Should().ContainSingle().Subject;
        audit.GetProperty("details").GetString().Should().Contain("revokedRefreshTokens");
        (await admin.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "the admin's own session is untouched");

        // The student signs in again from the new PC (re-binding); a second reset with no binding is 404 and revokes nothing.
        var fresh = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.WrongHwid, "NEW-PC");
        (await fresh.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        await _host.WithDbAsync(async db => (await db.DeviceBindings.AnyAsync(d => d.UserId == _host.Seed.StudentId)).Should().BeTrue());

        var student2Web = await _host.AsStudent2();
        (await admin.DeleteAsync($"/api/users/{_host.Seed.Student2Id}/device-binding")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await student2Web.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
