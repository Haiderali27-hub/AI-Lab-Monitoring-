using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// Exam-integrity regressions (verifier pass after cae6360):
///  • D-41 — a revoked, deactivated or re-roled staff member can no longer use an already open hub connection.
///  • D-42 — a student gets the questions only while sitting the exam.
///  • D-43 — student/start never opens a second sitting of a submitted / force-submitted exam.
///  • D-44 — a teacher cannot put another teacher's students on their exam.
///  • D-45 — with the desktop client required (the default) a web token cannot answer, submit or report for a session.
///  • D-46 — marking a proctor warning read in the student portal is audited.
///  • D-47 — a session's timeline shows only the warnings sent about that session.
/// </summary>
public class SessionIntegrityAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private static string TokenOf(HttpClient c) => c.DefaultRequestHeaders.Authorization!.Parameter!;

    // ── D-41: hub calls re-check the sign-in (real WebSocket connection) ──────

    [Fact]
    public async Task M10_I_21_OpenHubConnection_StopsWorking_AfterForceLogout_Deactivation_OrRoleChange()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var admin = await _host.AsAdmin();
        var tracker = _host.Services.GetRequiredService<ExamGroupTracker>();

        // 1. The owner teacher's Live Monitor socket: joined while signed in; after a force-logout the same socket
        //    can no longer unlock the student's PC, and the server closes it.
        var teacher = await _host.AsTeacher();
        await using (var hub = await RawHubClient.ConnectAsync(_host, TokenOf(teacher)))
        {
            (await hub.InvokeAsync("JoinExamGroup", _host.Seed.ExamId)).Should().Be(RawHubClient.Ok, "a signed-in owner may join");
            tracker.MembershipsOf(_host.Seed.TeacherId).Should().ContainSingle();

            (await admin.PostAsync($"/api/users/{_host.Seed.TeacherId}/force-logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            tracker.MembershipsOf(_host.Seed.TeacherId).Should().BeEmpty("the force-logout drops the group membership");

            (await hub.InvokeAsync("UnlockSession", sid, 5, "Keyboard broken")).Should().NotBe(RawHubClient.Ok);
            (await hub.InvokeAsync("JoinExamGroup", _host.Seed.ExamId)).Should().NotBe(RawHubClient.Ok);
            (await hub.IsClosedAsync()).Should().BeTrue("a refused call closes the connection");
            tracker.MembershipsOf(_host.Seed.TeacherId).Should().BeEmpty("the ex-session did not rejoin");
        }

        // 2. An assigned invigilator, deactivated while watching: cannot force-submit the student any more.
        var (invId, invEmail, invPassword) = await _host.CreateUserAsync("Invigilator");
        (await (await _host.AsTeacher()).PostAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/proctors", new { userId = invId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var invigilator = await _host.LoginAsync(invEmail, invPassword);
        await using (var hub = await RawHubClient.ConnectAsync(_host, TokenOf(invigilator)))
        {
            (await hub.InvokeAsync("JoinExamGroup", _host.Seed.ExamId)).Should().Be(RawHubClient.Ok);
            (await admin.PatchAsync($"/api/users/{invId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await hub.InvokeAsync("ForceSubmitSessionById", sid)).Should().NotBe(RawHubClient.Ok);
            (await hub.IsClosedAsync()).Should().BeTrue();
        }

        // 3. A role that changed without any token being revoked (defence in depth): the token's role no longer
        //    matches the account, so the old role's powers are gone at once.
        var (inv2Id, inv2Email, inv2Password) = await _host.CreateUserAsync("Invigilator");
        (await (await _host.AsTeacher()).PostAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/proctors", new { userId = inv2Id }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var invigilator2 = await _host.LoginAsync(inv2Email, inv2Password);
        await using (var hub = await RawHubClient.ConnectAsync(_host, TokenOf(invigilator2)))
        {
            (await hub.InvokeAsync("JoinExamGroup", _host.Seed.ExamId)).Should().Be(RawHubClient.Ok);
            await _host.WithDbAsync(async db =>
            {
                var u = await db.Users.SingleAsync(x => x.UserId == inv2Id);
                u.Role = UserRole.Student;
                await db.SaveChangesAsync();
            });
            (await hub.InvokeAsync("ExtendStudentTime", sid, 30)).Should().NotBe(RawHubClient.Ok);
        }

        // Nothing any of the three refused calls would have done happened.
        await _host.WithDbAsync(async db =>
        {
            var s = await db.ExamSessions.AsNoTracking().SingleAsync(x => x.SessionId == sid);
            s.Status.Should().Be(SessionStatus.InProgress);
            s.UnlockedUntil.Should().BeNull();
            s.ExtraMinutes.Should().Be(0);
            (await db.AuditLogs.AnyAsync(a => a.EventType == "SESSION_UNLOCKED" || a.EventType == "SESSION_FORCE_SUBMITTED" ||
                                              a.EventType == "SESSION_TIME_EXTENDED")).Should().BeFalse();
        });

        // A valid connection is unaffected: the admin still supervises through the hub.
        await using (var hub = await RawHubClient.ConnectAsync(_host, TokenOf(await _host.AsAdmin())))
        {
            (await hub.InvokeAsync("ExtendStudentTime", sid, 5)).Should().Be(RawHubClient.Ok);
        }
        (await _host.WithDbAsync(db => db.ExamSessions.AsNoTracking().SingleAsync(x => x.SessionId == sid))).ExtraMinutes.Should().Be(5);
    }

    // ── D-42: the paper only while sitting the exam ──────────────────────────

    [Fact]
    public async Task M3_I_08_StudentGetsTheQuestions_OnlyWhileSittingTheExam()
    {
        static async Task<int> QuestionCount(HttpClient c, Guid examId)
        {
            var resp = await c.GetAsync($"/api/exams/{examId}");
            resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
            var body = await TestHost.JsonAsync(resp);
            body.GetProperty("title").GetString().Should().NotBeNullOrEmpty("the exam's details are still shown");
            return body.GetProperty("questions").GetArrayLength();
        }

        var student = await _host.AsStudent();
        var student2 = await _host.AsStudent2();

        // Days before the exam: details, no questions.
        var future = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddDays(3));
        (await QuestionCount(student, future)).Should().Be(0);

        // Open now, but the student is ineligible: no questions (and no start).
        var open = await _host.CreateExamAsync(new[] { _host.Seed.StudentId, _host.Seed.Student2Id });
        await _host.WithDbAsync(async db =>
        {
            var a = await db.ExamAssignments.SingleAsync(x => x.ExamId == open && x.UserId == _host.Seed.Student2Id);
            a.IsEligible = false;
            await db.SaveChangesAsync();
        });
        (await QuestionCount(student2, open)).Should().Be(0);
        (await _host.StartSessionRawAsync(student2, open)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await QuestionCount(student2, open)).Should().Be(0);

        // Open and eligible, but not started yet: no questions. Started: the paper. Submitted: no questions again.
        (await QuestionCount(student, open)).Should().Be(0);
        var sid = await _host.StartSessionAsync(student, open);
        (await QuestionCount(student, open)).Should().Be(1);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await QuestionCount(student, open)).Should().Be(0);

        // Staff are unaffected.
        (await QuestionCount(await _host.AsTeacher(), future)).Should().Be(1);
    }

    // ── D-43: one sitting per exam on the desktop start path ─────────────────

    [Fact]
    public async Task M6_I_21_StudentStart_DoesNotReopen_ASubmittedOrForceSubmittedExam()
    {
        // Only this exam is open for the two students (the seeded exam would otherwise be the earliest candidate).
        await _host.WithDbAsync(async db =>
        {
            db.ExamAssignments.RemoveRange(db.ExamAssignments.Where(a => a.ExamId == _host.Seed.ExamId));
            await db.SaveChangesAsync();
        });
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId, _host.Seed.Student2Id });

        // Submitted by the student.
        var student = await _host.AsStudent();
        var first = await TestHost.DataAsync(await student.PostAsync("/api/exams/student/start", null));
        var sid = first.GetProperty("id").GetGuid();
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await student.PostAsync("/api/exams/student/start", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TestHost.ErrorCodeAsync(again)).Should().Be("ALREADY_SUBMITTED");

        // Force-submitted by the proctor: the student cannot undo it by starting again.
        var student2 = await _host.AsStudent2();
        var sid2 = (await TestHost.DataAsync(await student2.PostAsync("/api/exams/student/start", null))).GetProperty("id").GetGuid();
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.ForceSubmitSessionById(sid2);
        var reopen = await student2.PostAsync("/api/exams/student/start", null);
        reopen.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TestHost.ErrorCodeAsync(reopen)).Should().Be("ALREADY_SUBMITTED");

        await _host.WithDbAsync(async db =>
        {
            (await db.ExamSessions.CountAsync(s => s.ExamId == examId && s.UserId == _host.Seed.StudentId)).Should().Be(1);
            (await db.ExamSessions.CountAsync(s => s.ExamId == examId && s.UserId == _host.Seed.Student2Id)).Should().Be(1);
            (await db.ExamSessions.SingleAsync(s => s.SessionId == sid2)).Status.Should().Be(SessionStatus.ForceSubmitted);
            (await db.ExamSessions.AnyAsync(s => s.UserId == _host.Seed.StudentId && s.ExamId == examId && s.Status == SessionStatus.InProgress))
                .Should().BeFalse();
        });

        // The dashboard says so: the exam is Submitted and cannot be started.
        var current = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));
        current.GetProperty("examId").GetGuid().Should().Be(examId);
        current.GetProperty("status").GetString().Should().Be("Submitted");
        current.GetProperty("canStartNow").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task M3_I_09_AfterSubmitting_TheNextOpenExam_IsShownAndStartable()
    {
        // The seeded exam (assigned, open) would otherwise be the earliest candidate: take this student off it.
        await _host.WithDbAsync(async db =>
        {
            db.ExamAssignments.RemoveRange(db.ExamAssignments.Where(a => a.UserId == _host.Seed.StudentId));
            await db.SaveChangesAsync();
        });
        var first = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddMinutes(-10));
        var second = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddMinutes(-5));
        var student = await _host.AsStudent();

        var s1 = await TestHost.DataAsync(await student.PostAsync("/api/exams/student/start", null));
        var sid1 = s1.GetProperty("id").GetGuid();
        (await _host.WithDbAsync(db => db.ExamSessions.SingleAsync(s => s.SessionId == sid1))).ExamId.Should().Be(first);
        (await TestHost.SubmitAsync(student, sid1)).StatusCode.Should().Be(HttpStatusCode.OK);

        var current = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));
        current.GetProperty("examId").GetGuid().Should().Be(second);
        current.GetProperty("canStartNow").GetBoolean().Should().BeTrue();

        var s2 = await student.PostAsync("/api/exams/student/start", null);
        s2.StatusCode.Should().Be(HttpStatusCode.OK, await s2.Content.ReadAsStringAsync());
        var sid2 = (await TestHost.DataAsync(s2)).GetProperty("id").GetGuid();
        (await _host.WithDbAsync(db => db.ExamSessions.SingleAsync(s => s.SessionId == sid2))).ExamId.Should().Be(second);
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.ExamId == first))).Should().Be(1);
    }

    // ── D-44: no other teacher's students on your exam ───────────────────────

    [Fact]
    public async Task M9_I_21_Teacher_CannotAssign_AnotherTeachersStudents()
    {
        var (teacherBId, bEmail, bPassword) = await _host.CreateUserAsync("Teacher");
        var sectionB = Guid.NewGuid();
        await _host.WithDbAsync(async db =>
        {
            db.Sections.Add(new Section { SectionId = sectionB, CourseId = _host.Seed.CourseId, TeacherId = teacherBId, Name = "BSCS-6B", Semester = "Fall 2025" });
            await db.SaveChangesAsync();
        });
        var teacherB = await _host.LoginAsync(bEmail, bPassword);
        var seededStudent = _host.Seed.StudentId; // enrolled in the seeded teacher's section only
        var (unenrolledId, _, _) = await _host.CreateUserAsync("Student");

        Dictionary<string, object?> Payload(params Guid[] studentIds) => new()
        {
            ["title"] = "Teacher B's exam", ["instructions"] = "x", ["sectionId"] = sectionB,
            ["startTime"] = DateTime.UtcNow.AddMinutes(30), ["durationMinutes"] = 60, ["allowedApps"] = "[]",
            ["questions"] = new[] { TestHost.TheoryQuestion() }, ["studentIds"] = studentIds
        };

        // Create with teacher A's student: refused, nothing created.
        var refused = await teacherB.PostAsJsonAsync("/api/exams", Payload(seededStudent));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(refused)).Should().Be("STUDENT_NOT_IN_YOUR_SECTIONS");
        (await _host.WithDbAsync(db => db.Exams.AnyAsync(e => e.SectionId == sectionB))).Should().BeFalse();

        // A student enrolled in no section may still be assigned (documented choice).
        var created = await teacherB.PostAsJsonAsync("/api/exams", Payload(unenrolledId));
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var examB = (await TestHost.JsonAsync(created)).GetProperty("examId").GetGuid();

        // Adding teacher A's student later: refused on both paths.
        var add = await teacherB.PostAsJsonAsync($"/api/exams/{examB}/assignments", new { userId = seededStudent });
        add.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(add)).Should().Be("STUDENT_NOT_IN_YOUR_SECTIONS");
        (await teacherB.PutAsJsonAsync($"/api/exams/{examB}", Payload(unenrolledId, seededStudent))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.WithDbAsync(db => db.ExamAssignments.AnyAsync(a => a.ExamId == examB && a.UserId == seededStudent))).Should().BeFalse();

        // An admin may assign anyone; teacher B can then still edit the exam, keeping that student on it.
        (await (await _host.AsAdmin()).PostAsJsonAsync($"/api/exams/{examB}/assignments", new { userId = seededStudent }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacherB.PutAsJsonAsync($"/api/exams/{examB}", Payload(unenrolledId, seededStudent))).StatusCode.Should().Be(HttpStatusCode.OK);

        // A student who is also in one of teacher B's sections may be assigned by B.
        var (sharedId, _, _) = await _host.CreateUserAsync("Student");
        await _host.WithDbAsync(async db =>
        {
            db.SectionEnrollments.AddRange(
                new SectionEnrollment { SectionId = _host.Seed.SectionId, UserId = sharedId },
                new SectionEnrollment { SectionId = sectionB, UserId = sharedId });
            await db.SaveChangesAsync();
        });
        (await teacherB.PostAsJsonAsync($"/api/exams/{examB}/assignments", new { userId = sharedId })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── D-45: the exam is the desktop app's, not the web token's ─────────────

    [Fact]
    public void M9_U_07_DesktopClient_IsRequired_ByDefault()
    {
        var empty = new ConfigurationBuilder().Build();
        new SeatGuard(empty, null!, null!).DesktopClientRequired.Should().BeTrue();
        var off = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Exams:RequireDesktopClientForStart"] = "false" }).Build();
        new SeatGuard(off, null!, null!).DesktopClientRequired.Should().BeFalse();
    }

    [Fact]
    public async Task M9_I_22_WebToken_CannotReadQuestions_Answer_Submit_OrReport_ForTheDesktopSession()
    {
        using var strict = new TestHost(b => b.UseSetting("Exams:RequireDesktopClientForStart", "true"));
        var examId = strict.Seed.ExamId;
        var desktop = await strict.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-01");
        var sid = await strict.StartSessionAsync(desktop, examId);
        var qid = (await strict.QuestionIdsAsync(examId))[0];
        (await TestHost.SaveAnswerAsync(desktop, sid, qid, "desktop answer", new { revision = 1 })).StatusCode.Should().Be(HttpStatusCode.OK);

        // The accomplice: the same student's password on the web portal.
        var web = await strict.AsStudent();
        async Task ExpectRefused(Task<HttpResponseMessage> call, string what)
        {
            var resp = await call;
            resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, what);
            (await TestHost.ErrorCodeAsync(resp)).Should().Be("DESKTOP_CLIENT_REQUIRED", what);
        }
        await ExpectRefused(TestHost.SaveAnswerAsync(web, sid, qid, "accomplice answer", new { revision = 99 }), "save-answer");
        await ExpectRefused(web.GetAsync($"/api/exams/sessions/{sid}/answers"), "answers");
        await ExpectRefused(TestHost.HeartbeatAsync(web, sid), "heartbeat");
        await ExpectRefused(TestHost.PostEventAsync(web, sid, "Violation", "{\"reason\":\"FocusLost\"}"), "event");
        await ExpectRefused(web.PostAsJsonAsync("/api/monitoring/events/batch", new { examSessionId = sid, events = Array.Empty<object>() }), "batch");
        await ExpectRefused(TestHost.SubmitAsync(web, sid), "submit");
        var paper = await TestHost.JsonAsync(await web.GetAsync($"/api/exams/{examId}"));
        paper.GetProperty("questions").GetArrayLength().Should().Be(0, "the web token gets no questions");

        // The desktop app still has everything; the stored answer is the desktop's and the session is still running.
        (await TestHost.JsonAsync(await desktop.GetAsync($"/api/exams/{examId}"))).GetProperty("questions").GetArrayLength().Should().BeGreaterThan(0);
        (await TestHost.HeartbeatAsync(desktop, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        await strict.WithDbAsync(async db =>
        {
            var answer = await db.Answers.AsNoTracking().SingleAsync(a => a.SessionId == sid && a.QuestionId == qid);
            answer.AnswerText.Should().Be("desktop answer");
            answer.Revision.Should().Be(1);
            (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).Status.Should().Be(SessionStatus.InProgress);
            (await db.MonitoringEvents.AnyAsync(e => e.ExamSessionId == sid && e.EventType != MonitoringEventType.Heartbeat)).Should().BeFalse();
        });
        (await TestHost.SubmitAsync(desktop, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── D-46: acknowledging a warning in the student portal is audited ───────

    [Fact]
    public async Task M10_I_22_StudentPortalRead_OfAProctorWarning_IsAudited()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.SendWarning(_host.Seed.StudentId.ToString(), "Eyes on your own screen");
        var info = new Models.Notifications.Notification
        {
            NotificationId = Guid.NewGuid(), RecipientId = _host.Seed.StudentId, Title = "Info", Body = "Room changed",
            Type = "Announcement", IsRead = false, CreatedAt = DateTime.UtcNow
        };
        await _host.WithDbAsync(async db => { db.Notifications.Add(info); await db.SaveChangesAsync(); });
        var warningId = await _host.WithDbAsync(db => db.Notifications.Where(n => n.Type == "Warning").Select(n => n.NotificationId).SingleAsync());

        (await student.PatchAsync($"/api/student/notifications/{warningId}/read", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.PatchAsync($"/api/student/notifications/{warningId}/read", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.PatchAsync($"/api/student/notifications/{info.NotificationId}/read", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var acks = await _host.AuditLogsAsync("PROCTOR_WARNING_ACKNOWLEDGED");
        acks.Should().ContainSingle("only the first read of the warning acknowledges it; an announcement is not a warning");
        var ack = acks[0].GetRawText();
        ack.Should().Contain(warningId.ToString()).And.Contain("student-portal").And.Contain(sid.ToString());
        // And it no longer rides on the heartbeat ack (it was seen).
        var beat = await TestHost.DataAsync(await TestHost.HeartbeatAsync(student, sid));
        beat.GetRawText().Should().NotContain("Eyes on your own screen");
    }

    // ── D-47: a session's timeline carries only that session's warnings ──────

    [Fact]
    public async Task M5_I_23_Timeline_ShowsOnlyTheWarningsAboutThatSession()
    {
        // Exam A: the seeded teacher's. Exam B: teacher B's practice exam, with the same student (assigned by an admin).
        var (teacherBId, bEmail, bPassword) = await _host.CreateUserAsync("Teacher");
        var sectionB = Guid.NewGuid();
        await _host.WithDbAsync(async db =>
        {
            db.Sections.Add(new Section { SectionId = sectionB, CourseId = _host.Seed.CourseId, TeacherId = teacherBId, Name = "BSCS-6C", Semester = "Fall 2025" });
            await db.SaveChangesAsync();
        });
        var admin = await _host.AsAdmin();
        var create = await admin.PostAsJsonAsync("/api/exams", new Dictionary<string, object?>
        {
            ["title"] = "Practice B", ["instructions"] = "x", ["sectionId"] = sectionB, ["startTime"] = DateTime.UtcNow.AddMinutes(-1),
            ["durationMinutes"] = 60, ["allowedApps"] = "[]", ["isMock"] = true,
            ["questions"] = new[] { TestHost.TheoryQuestion() }, ["studentIds"] = new[] { _host.Seed.StudentId }
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var examB = (await TestHost.JsonAsync(create)).GetProperty("examId").GetGuid();

        var student = await _host.AsStudent();
        var sidA = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var sidB = await _host.StartSessionAsync(student, examB);

        using (var hubB = _host.CreateHub(TestHost.Principal(teacherBId, "Teacher")))
            await hubB.Hub.SendWarning(_host.Seed.StudentId.ToString(), "Practice B: close the browser");
        using (var hubA = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hubA.Hub.SendWarning(_host.Seed.StudentId.ToString(), "Exam A: eyes on your screen");

        var teacherA = await _host.AsTeacher();
        var timelineA = await (await teacherA.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sidA}/timeline")).Content.ReadAsStringAsync();
        timelineA.Should().Contain("Exam A: eyes on your screen").And.NotContain("Practice B", "exam B's warning is not exam A's evidence");

        var teacherB = await _host.LoginAsync(bEmail, bPassword);
        var timelineB = await (await teacherB.GetAsync($"/api/exams/{examB}/sessions/{sidB}/timeline")).Content.ReadAsStringAsync();
        timelineB.Should().Contain("Practice B: close the browser").And.NotContain("Exam A:");
    }
}

/// <summary>
/// A minimal SignalR client (JSON hub protocol over a TestServer WebSocket, negotiation skipped) — the test project has
/// no SignalR client package. Enough to call hub methods on one real connection and see how the server answers.
/// </summary>
internal sealed class RawHubClient : IAsyncDisposable
{
    public const string Ok = "ok";
    public const string Closed = "closed";
    private const char RecordSeparator = '\u001e';

    private readonly WebSocket _socket;
    private readonly StringBuilder _buffer = new();
    private readonly Queue<string> _messages = new();
    private int _nextId;
    private bool _closed;

    private RawHubClient(WebSocket socket) => _socket = socket;

    public static async Task<RawHubClient> ConnectAsync(TestHost host, string accessToken)
    {
        var ws = host.App.Server.CreateWebSocketClient();
        var socket = await ws.ConnectAsync(new Uri($"ws://localhost/hubs/monitoring?access_token={Uri.EscapeDataString(accessToken)}"), CancellationToken.None);
        var client = new RawHubClient(socket);
        await client.SendAsync("{\"protocol\":\"json\",\"version\":1}");
        var handshake = await client.ReceiveAsync();
        handshake.Should().NotBeNull("the server answers the handshake");
        handshake!.Should().NotContain("error", "the handshake is accepted");
        return client;
    }

    /// <summary>Invokes a hub method. Returns <see cref="Ok"/>, the server's error text, or <see cref="Closed"/>.</summary>
    public async Task<string> InvokeAsync(string target, params object[] args)
    {
        if (_closed) return Closed;
        var id = (++_nextId).ToString();
        var json = JsonSerializer.Serialize(new { type = 1, invocationId = id, target, arguments = args });
        try
        {
            await SendAsync(json);
        }
        catch (Exception e) when (e is WebSocketException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            _closed = true;
            return Closed;
        }
        while (true)
        {
            var message = await ReceiveAsync();
            if (message is null) return Closed;
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetInt32();
            if (type == 7) { _closed = true; return Closed; } // Close
            if (type != 3 || root.GetProperty("invocationId").GetString() != id) continue; // ping, broadcasts
            return root.TryGetProperty("error", out var error) ? error.GetString() ?? "error" : Ok;
        }
    }

    /// <summary>True once the server has closed the connection (reads until close, ignoring pings).</summary>
    public async Task<bool> IsClosedAsync()
    {
        while (!_closed)
        {
            var message = await ReceiveAsync();
            if (message is null) break;
            if (message.Contains("\"type\":7")) _closed = true;
        }
        return _closed;
    }

    private async Task SendAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + RecordSeparator);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cts.Token);
    }

    // Next complete hub message, or null when the socket closed (or nothing arrived within 10 s).
    private async Task<string?> ReceiveAsync()
    {
        var chunk = new byte[8192];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (_messages.Count > 0) return _messages.Dequeue();
            if (_closed) return null;
            WebSocketReceiveResult result;
            try
            {
                result = await _socket.ReceiveAsync(chunk, cts.Token);
            }
            catch (Exception e) when (e is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException)
            {
                _closed = true;
                return null;
            }
            if (result.MessageType == WebSocketMessageType.Close)
            {
                _closed = true;
                return _messages.Count > 0 ? _messages.Dequeue() : null;
            }
            _buffer.Append(Encoding.UTF8.GetString(chunk, 0, result.Count));
            var text = _buffer.ToString();
            int sep;
            while ((sep = text.IndexOf(RecordSeparator)) >= 0)
            {
                _messages.Enqueue(text[..sep]);
                text = text[(sep + 1)..];
            }
            _buffer.Clear().Append(text);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
        }
        catch
        {
            // already gone
        }
        _socket.Dispose();
    }
}
