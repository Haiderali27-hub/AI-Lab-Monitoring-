using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M10 — Live Proctoring Dashboard (TEST_CASES.md §M10, layer I).
/// Hub tests instantiate MonitoringHub directly with a fake caller context (no SignalR client package needed).
/// Contract: IMPLEMENTATION_PLAN.md §4.3 — POST/PATCH /api/incidents, GET /api/exams/{id}/incidents, hub UnlockSession /
/// RelockSession / ExtendStudentTime / SendWarning, ExamProctors (POST /api/exams/{id}/proctors), the board snapshot
/// GET /api/exams/{id}/live and the session's apps GET /api/exams/{id}/sessions/{sid}/processes.
/// </summary>
public class Module10ProctoringAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private async Task<Guid> StudentSessionAsync() => await _host.StartSessionAsync(await _host.AsStudent(), _host.Seed.ExamId);

    private static async Task<Guid> CreateIncidentAsync(HttpClient c, Guid sessionId)
    {
        var resp = await c.PostAsJsonAsync("/api/incidents", new { sessionId, severity = "High", notes = "Phone visible on desk" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await TestHost.DataAsync(resp)).GetProperty("incidentId").GetGuid();
    }

    private static Task<HttpResponseMessage> PatchIncidentAsync(HttpClient c, Guid id, string status) =>
        c.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/incidents/{id}") { Content = JsonContent.Create(new { status, notes = $"→ {status}" }) });

    [Fact]
    public async Task M10_I_01_CreateIncident_Returns201_AndAudited()
    {
        var sid = await StudentSessionAsync();
        var teacher = await _host.AsTeacher();

        var id = await CreateIncidentAsync(teacher, sid);
        id.Should().NotBeEmpty();

        var logs = await _host.AuditLogsAsync("INCIDENT_CREATED");
        logs.Should().ContainSingle().Which.GetProperty("entityId").GetString().Should().Be(id.ToString());
    }

    [Fact]
    public async Task M10_I_02_IncidentEscalateResolve_StoresTransitions_InvalidTransitions400()
    {
        var sid = await StudentSessionAsync();
        var teacher = await _host.AsTeacher();
        var id = await CreateIncidentAsync(teacher, sid);

        (await PatchIncidentAsync(teacher, id, "Escalated")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PatchIncidentAsync(teacher, id, "Resolved")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Resolved is terminal; unknown statuses are rejected.
        (await PatchIncidentAsync(teacher, id, "Escalated")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PatchIncidentAsync(teacher, id, "Banana")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var incident = await _host.WithDbAsync(db => Task.FromResult(Planned.Set(db, "Incidents").Single()));
        Planned.Get(incident, "Status")!.ToString().Should().Be("Resolved");
        (await _host.AuditLogsAsync()).Count(l => l.GetProperty("eventType").GetString()!.StartsWith("INCIDENT_")).Should().Be(3);
    }

    [Fact]
    public async Task M10_I_03_TeacherOfOtherSection_CannotCreateOrUpdateIncident_403()
    {
        var sid = await StudentSessionAsync();
        var owner = await _host.AsTeacher();
        var id = await CreateIncidentAsync(owner, sid);

        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        var outsider = await _host.LoginAsync(email, password);
        (await outsider.PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Low", notes = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PatchIncidentAsync(outsider, id, "Resolved")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Students can't file incidents at all.
        (await (await _host.AsStudent()).PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Low", notes = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task M10_I_04_UnlockSession_SendsEventToStudent_AndIsAudited()
    {
        var sid = await StudentSessionAsync();

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await Planned.InvokeHubAsync(hub.Hub, "UnlockSession", sid, 5, "Invigilator fixing keyboard");

        _host.Hub.Messages.Should().Contain(m => m.Method == "SessionUnlocked" && m.Target.Contains(_host.Seed.StudentId.ToString())
                                                 && m.ArgsJson.Contains("5"));
        var logs = await _host.AuditLogsAsync("SESSION_UNLOCKED");
        logs.Should().ContainSingle();
        logs[0].GetRawText().Should().Contain("Invigilator fixing keyboard");

        // Students cannot unlock themselves.
        using var studentHub = _host.CreateHub(TestHost.Principal(_host.Seed.StudentId, "Student"));
        var act = () => Planned.InvokeHubAsync(studentHub.Hub, "UnlockSession", sid, 60, "self");
        await act.Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task M10_I_05_ExtendSingleStudent_OnlyThatSessionsEndMoves()
    {
        var s1 = await _host.AsStudent();
        var sid1 = await _host.StartSessionAsync(s1, _host.Seed.ExamId);
        var s2 = await _host.AsStudent2();
        var sid2 = await _host.StartSessionAsync(s2, _host.Seed.ExamId);

        async Task<int> Remaining(HttpClient c, Guid sid) =>
            (await TestHost.DataAsync(await TestHost.HeartbeatAsync(c, sid))).GetProperty("remainingSeconds").GetInt32();

        var before1 = await Remaining(s1, sid1);
        var before2 = await Remaining(s2, sid2);

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await Planned.InvokeHubAsync(hub.Hub, "ExtendStudentTime", sid1, 10);

        (await Remaining(s1, sid1) - before1).Should().BeInRange(590, 610);
        (await Remaining(s2, sid2) - before2).Should().BeInRange(-10, 0);
        (await _host.WithDbAsync(db => db.Exams.SingleAsync(e => e.ExamId == _host.Seed.ExamId))).DurationMinutes.Should().Be(60);
    }

    [Fact]
    public async Task M10_I_06_StudentJoinExamGroup_ThrowsHubException()
    {
        using var hub = _host.CreateHub(TestHost.Principal(_host.Seed.StudentId, "Student"));

        var act = () => hub.Hub.JoinExamGroup(_host.Seed.ExamId);

        await act.Should().ThrowAsync<HubException>();
        hub.Groups.Added.Should().BeEmpty("a student must never be put in a proctor group");

        // Students can't send warnings or force-submit either.
        await ((Func<Task>)(() => hub.Hub.SendWarning(_host.Seed.Student2Id.ToString(), "spoof"))).Should().ThrowAsync<HubException>();
        await ((Func<Task>)(() => hub.Hub.ForceSubmitSession(_host.Seed.Student2Id.ToString()))).Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task M10_I_06b_TeacherJoinExamGroup_IsAddedToThatExamsGroup()
    {
        using var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher"));
        await hub.Hub.JoinExamGroup(_host.Seed.ExamId);
        hub.Groups.Added.Should().ContainSingle().Which.Group.Should().Be($"exam:{_host.Seed.ExamId}");
    }

    [Fact]
    public async Task M10_I_07_TeacherCannotForceSubmitOtherTeachersExam_HubException()
    {
        var sid = await StudentSessionAsync();
        var (otherTeacherId, _, _) = await _host.CreateUserAsync("Teacher");

        using (var hub = _host.CreateHub(TestHost.Principal(otherTeacherId, "Teacher")))
        {
            var act = () => hub.Hub.ForceSubmitSession(_host.Seed.StudentId.ToString());
            await act.Should().ThrowAsync<HubException>();
        }

        var status = await _host.WithDbAsync(async db => (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).Status);
        status.Should().Be(SessionStatus.InProgress);
        (await _host.WithDbAsync(db => db.AiGradingResults.CountAsync())).Should().Be(0);

        // An admin may act on any exam.
        using (var adminHub = _host.CreateHub(TestHost.Principal(_host.Seed.AdminId, "Admin")))
            await adminHub.Hub.ForceSubmitSession(_host.Seed.StudentId.ToString());
        (await _host.WithDbAsync(async db => (await db.ExamSessions.AsNoTracking().SingleAsync(s => s.SessionId == sid)).Status))
            .Should().Be(SessionStatus.ForceSubmitted);
    }

    [Fact]
    public async Task M10_I_08_ProctorAssignment_LetsInvigilatorJoinGroup_OnlyForThatExam()
    {
        var (invigilatorId, _, _) = await _host.CreateUserAsync("Invigilator");
        var principal = TestHost.Principal(invigilatorId, "Invigilator");

        using (var before = _host.CreateHub(principal))
            await ((Func<Task>)(() => before.Hub.JoinExamGroup(_host.Seed.ExamId))).Should().ThrowAsync<HubException>("not yet assigned as proctor");

        var teacher = await _host.AsTeacher();
        (await teacher.PostAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/proctors", new { userId = invigilatorId }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        using var after = _host.CreateHub(principal);
        await after.Hub.JoinExamGroup(_host.Seed.ExamId);
        after.Groups.Added.Should().ContainSingle().Which.Group.Should().Be($"exam:{_host.Seed.ExamId}");

        var otherExam = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });
        await ((Func<Task>)(() => after.Hub.JoinExamGroup(otherExam))).Should().ThrowAsync<HubException>();
    }

    // ── Stage S6 additions ────────────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> HeartbeatWithAppsAsync(HttpClient student, Guid sessionId, string apps) =>
        student.PostAsJsonAsync("/api/monitoring/heartbeat", new
        {
            examSessionId = sessionId, isForegroundExamApp = true, activeWindowTitle = "SmartExam", processListSnapshot = apps
        });

    private static async Task<JsonElement> AckAsync(HttpClient student, Guid sessionId)
    {
        var resp = await TestHost.HeartbeatAsync(student, sessionId);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return await TestHost.DataAsync(resp);
    }

    private static Task<HttpResponseMessage> UploadScreenAsync(HttpClient student, Guid sessionId)
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9 };
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(sessionId.ToString()), "examSessionId");
        form.Add(new StringContent("Screen"), "kind");
        form.Add(new StringContent(DateTime.UtcNow.ToString("O")), "capturedAt");
        var file = new ByteArrayContent(jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "screen.jpg");
        return student.PostAsync("/api/monitoring/screenshot", form);
    }

    private async Task<HttpClient> AssignedInvigilatorAsync(Guid examId)
    {
        var (id, email, password) = await _host.CreateUserAsync("Invigilator");
        (await (await _host.AsTeacher()).PostAsJsonAsync($"/api/exams/{examId}/proctors", new { userId = id }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        return await _host.LoginAsync(email, password);
    }

    [Fact]
    public async Task M10_I_09_SendWarning_PushesToStudent_AndRidesHeartbeatAckUntilAcknowledged()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.SendWarning(_host.Seed.StudentId.ToString(), "  Eyes on your own screen  ");

        // Pushed to the student's own connections (D-05) and to the exam's other proctors.
        _host.Hub.Messages.Should().Contain(m => m.Method == "ProctorWarning" && m.Target == $"user:{_host.Seed.StudentId}"
                                                 && m.ArgsJson.Contains("Eyes on your own screen"));
        _host.Hub.Messages.Should().Contain(m => m.Method == "ProctorWarningSent" && m.Target == $"exam:{_host.Seed.ExamId}");
        var sent = await _host.AuditLogsAsync("PROCTOR_WARNING_SENT");
        sent.Should().ContainSingle().Which.GetProperty("entityId").GetString().Should().Be(sid.ToString());

        // The desktop client has no SignalR connection: the heartbeat ack carries the unread warning ...
        var warnings = (await AckAsync(student, sid)).GetProperty("warnings");
        warnings.GetArrayLength().Should().Be(1);
        warnings[0].GetProperty("title").GetString().Should().Be("Proctor Warning");
        warnings[0].GetProperty("body").GetString().Should().Be("Eyes on your own screen");
        var notificationId = warnings[0].GetProperty("notificationId").GetGuid();
        (await AckAsync(student, sid)).GetProperty("warnings").GetArrayLength().Should().Be(1, "it repeats until acknowledged");

        // ... until the student acknowledges it on the banner (audited as evidence that it was seen).
        (await student.PatchAsync($"/api/notifications/{notificationId}/read", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AckAsync(student, sid)).GetProperty("warnings").GetArrayLength().Should().Be(0);
        (await _host.AuditLogsAsync("PROCTOR_WARNING_ACKNOWLEDGED")).Should().ContainSingle();

        // A teacher of another section cannot warn this student; an empty warning is refused.
        var (outsiderId, _, _) = await _host.CreateUserAsync("Teacher");
        using (var outsider = _host.CreateHub(TestHost.Principal(outsiderId, "Teacher")))
            await ((Func<Task>)(() => outsider.Hub.SendWarning(_host.Seed.StudentId.ToString(), "x"))).Should().ThrowAsync<HubException>();
        using (var teacherHub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await ((Func<Task>)(() => teacherHub.Hub.SendWarning(_host.Seed.StudentId.ToString(), "   "))).Should().ThrowAsync<HubException>();
        (await _host.AuditLogsAsync("PROCTOR_WARNING_SENT")).Should().ContainSingle();
    }

    [Fact]
    public async Task M10_I_10_Unlock_IsInHeartbeatAck_Relock_EndsIt_AndLimitsAreEnforced()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await AckAsync(student, sid)).GetProperty("unlockedUntilUtc").ValueKind.Should().Be(JsonValueKind.Null);

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
        {
            await ((Func<Task>)(() => hub.Hub.UnlockSession(sid, 31, "too long"))).Should().ThrowAsync<HubException>();
            await ((Func<Task>)(() => hub.Hub.UnlockSession(sid, 0, "too short"))).Should().ThrowAsync<HubException>();
            await ((Func<Task>)(() => hub.Hub.UnlockSession(sid, 5, "  "))).Should().ThrowAsync<HubException>("the reason is audited, so it is required");
            await hub.Hub.UnlockSession(sid, 5, "Keyboard replaced");
        }

        var until = (await AckAsync(student, sid)).GetProperty("unlockedUntilUtc").GetDateTime().ToUniversalTime();
        until.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(30));
        _host.Hub.Messages.Should().Contain(m => m.Method == "SessionUnlocked" && m.Target == $"exam:{_host.Seed.ExamId}");

        // Only someone supervising the exam may unlock.
        var (outsiderId, _, _) = await _host.CreateUserAsync("Teacher");
        using (var outsider = _host.CreateHub(TestHost.Principal(outsiderId, "Teacher")))
            await ((Func<Task>)(() => outsider.Hub.UnlockSession(sid, 5, "x"))).Should().ThrowAsync<HubException>();

        // Relock ends it early; the client sees no deadline on its next beat.
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.RelockSession(sid);
        (await AckAsync(student, sid)).GetProperty("unlockedUntilUtc").ValueKind.Should().Be(JsonValueKind.Null);
        (await _host.AuditLogsAsync("SESSION_RELOCKED")).Should().ContainSingle();
        _host.Hub.Messages.Should().Contain(m => m.Method == "SessionRelocked" && m.Target == $"user:{_host.Seed.StudentId}");

        // A finished session cannot be unlocked.
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await ((Func<Task>)(() => hub.Hub.UnlockSession(sid, 5, "late"))).Should().ThrowAsync<HubException>();
        (await _host.AuditLogsAsync("SESSION_UNLOCKED")).Should().ContainSingle();
    }

    [Fact]
    public async Task M10_I_11_LiveSnapshot_ListsHeartbeatingStudents_WithEvidence_AndIsScoped()
    {
        var s1 = await _host.AsStudent();
        var sid1 = await _host.StartSessionAsync(s1, _host.Seed.ExamId);
        var s2 = await _host.AsStudent2();
        await _host.StartSessionAsync(s2, _host.Seed.ExamId);   // started, never heartbeated: not on the board yet

        (await HeartbeatWithAppsAsync(s1, sid1, "SmartExam,code")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.PostEventAsync(s1, sid1, "KeyBlocked", "{\"detail\":\"Alt+Tab\"}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadScreenAsync(s1, sid1)).StatusCode.Should().Be(HttpStatusCode.OK);
        var teacher = await _host.AsTeacher();
        await CreateIncidentAsync(teacher, sid1);

        var resp = await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/live");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var students = (await TestHost.JsonAsync(resp)).GetProperty("students");
        students.GetArrayLength().Should().Be(1);
        var tile = students[0];
        tile.GetProperty("userId").GetGuid().Should().Be(_host.Seed.StudentId);
        tile.GetProperty("sessionId").GetGuid().Should().Be(sid1);
        tile.GetProperty("sessionStatus").GetString().Should().Be("InProgress");
        tile.GetProperty("violationCount").GetInt32().Should().Be(1);
        tile.GetProperty("tileStatus").GetString().Should().Be("Warning");
        tile.GetProperty("lastViolationType").GetString().Should().Be("KeyBlocked");
        tile.GetProperty("processes").GetString().Should().Be("SmartExam,code");
        tile.GetProperty("totalQuestions").GetInt32().Should().Be((await _host.QuestionIdsAsync(_host.Seed.ExamId)).Count);
        tile.GetProperty("latestScreenshot").GetProperty("screenshotId").GetGuid().Should().NotBeEmpty();
        tile.GetProperty("openIncidents").GetInt32().Should().Be(1);
        tile.GetProperty("isDisconnected").GetBoolean().Should().BeFalse();

        // Scoped like every evidence endpoint: another section's teacher and students get nothing.
        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        (await (await _host.LoginAsync(email, password)).GetAsync($"/api/exams/{_host.Seed.ExamId}/live"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s1.GetAsync($"/api/exams/{_host.Seed.ExamId}/live")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task M10_I_12_Incidents_ListedPerExam_Broadcast_ProctorsMayFile_NotesValidated()
    {
        var sid = await StudentSessionAsync();
        var teacher = await _host.AsTeacher();
        var id = await CreateIncidentAsync(teacher, sid);
        _host.Hub.Messages.Should().Contain(m => m.Method == "IncidentChanged" && m.Target == $"exam:{_host.Seed.ExamId}"
                                                 && m.ArgsJson.Contains(id.ToString()));

        // An assigned invigilator may file one; an unassigned one may not.
        var proctor = await AssignedInvigilatorAsync(_host.Seed.ExamId);
        await CreateIncidentAsync(proctor, sid);
        var (_, email, password) = await _host.CreateUserAsync("Invigilator");
        var stranger = await _host.LoginAsync(email, password);
        (await stranger.PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Low", notes = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.GetAsync($"/api/exams/{_host.Seed.ExamId}/incidents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Validation: unknown session (indistinguishable from a foreign one), bad severity, empty notes.
        (await teacher.PostAsJsonAsync("/api/incidents", new { sessionId = Guid.NewGuid(), severity = "Low", notes = "x" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var bad = await teacher.PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Apocalyptic", notes = "x" });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(bad)).Should().Be("INVALID_SEVERITY");
        (await teacher.PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Low", notes = "   " }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await teacher.PostAsJsonAsync("/api/incidents", new { sessionId = sid, severity = "Low", notes = new string('n', 2001) }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Notes-only edit keeps the status and is audited as an update.
        var edit = await teacher.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/incidents/{id}")
        { Content = JsonContent.Create(new { notes = "Phone was in the bag, not on the desk" }) });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        var edited = await TestHost.JsonAsync(edit);
        edited.GetProperty("status").GetString().Should().Be("Open");
        edited.GetProperty("notes").GetString().Should().Be("Phone was in the bag, not on the desk");
        (await _host.AuditLogsAsync("INCIDENT_UPDATED")).Should().ContainSingle();

        // Resolve with a note; the list (newest first) shows both incidents with the student's name.
        (await PatchIncidentAsync(teacher, id, "Resolved")).StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await TestHost.JsonAsync(await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/incidents"));
        list.GetArrayLength().Should().Be(2);
        list.EnumerateArray().Should().OnlyContain(i => i.GetProperty("studentName").GetString()!.Length > 0);
        var resolved = list.EnumerateArray().Single(i => i.GetProperty("incidentId").GetGuid() == id);
        resolved.GetProperty("status").GetString().Should().Be("Resolved");
        resolved.GetProperty("statusNote").GetString().Should().Be("→ Resolved");
        resolved.GetProperty("resolvedByName").GetString().Should().NotBeNullOrEmpty();
        (await TestHost.JsonAsync(await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/incidents?status=Open")))
            .GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task M10_I_13_SessionProcesses_RemembersAppsSeenDuringTheSession()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await HeartbeatWithAppsAsync(student, sid, "SmartExam,chrome")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await HeartbeatWithAppsAsync(student, sid, "SmartExam,code")).StatusCode.Should().Be(HttpStatusCode.OK);

        var teacher = await _host.AsTeacher();
        var resp = await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/processes");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHost.JsonAsync(resp);
        body.GetProperty("current").EnumerateArray().Select(e => e.GetString()).Should().Equal("code", "SmartExam");
        var seen = body.GetProperty("seen").EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!, e => e.GetProperty("running").GetBoolean());
        seen.Should().ContainKey("chrome").WhoseValue.Should().BeFalse("chrome was closed again but was seen during the exam");
        seen["code"].Should().BeTrue();

        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        (await (await _host.LoginAsync(email, password)).GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/processes"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task M10_I_14_SessionTimeline_ShowsIncidentsAndProctorActions()
    {
        var sid = await StudentSessionAsync();
        var teacher = await _host.AsTeacher();
        await CreateIncidentAsync(teacher, sid);
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.UnlockSession(sid, 5, "Mouse replaced");

        var items = (await TestHost.JsonAsync(await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline")))
            .EnumerateArray().ToList();
        items.Should().Contain(i => i.GetProperty("kind").GetString() == "Incident" && i.GetProperty("detail").GetString() == "Phone visible on desk"
                                    && i.GetProperty("severity").GetString() == "Violation");
        items.Should().Contain(i => i.GetProperty("kind").GetString() == "Action" && i.GetProperty("type").GetString() == "SESSION_UNLOCKED"
                                    && i.GetProperty("detail").GetString()!.Contains("Mouse replaced"));
    }

    // ── S6 verifier additions ─────────────────────────────────────────────────────────────────────

    private Dictionary<string, object?> ExamPayload(DateTime startUtc) => new()
    {
        ["title"] = "Moved exam",
        ["instructions"] = "x",
        ["sectionId"] = _host.Seed.SectionId,
        ["startTime"] = startUtc,
        ["durationMinutes"] = 60,
        ["allowedApps"] = "[\"code.exe\"]",
        ["aiEvaluationEnabled"] = false,
        ["plagiarismThreshold"] = 70,
        ["questions"] = new[] { TestHost.TheoryQuestion() },
        ["studentIds"] = Array.Empty<Guid>()
    };

    [Fact]
    public async Task M10_I_15_WholeExamActions_AreOwnerOrAdminOnly_ProctorsAndOtherTeachersGet403()
    {
        // IMPLEMENTATION_PLAN §4.3 / Module 7 notes: extend-time, force-submit-all, edit and delete were callable by
        // any teacher for any exam. An unrelated teacher and a co-proctoring teacher are both refused.
        var running = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, running);
        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        var outsider = await _host.LoginAsync(email, password);
        var (coId, coEmail, coPassword) = await _host.CreateUserAsync("Teacher");
        (await (await _host.AsTeacher()).PostAsJsonAsync($"/api/exams/{running}/proctors", new { userId = coId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var coProctor = await _host.LoginAsync(coEmail, coPassword);

        foreach (var caller in new[] { outsider, coProctor })
        {
            (await caller.PostAsJsonAsync($"/api/exams/{running}/extend-time", new { additionalMinutes = 30 }))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await caller.PostAsync($"/api/exams/{running}/force-submit-all", null))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        // Nothing changed: same duration, the exam still runs, the student can still save.
        var owner = await _host.AsTeacher();
        var detail = await TestHost.JsonAsync(await owner.GetAsync($"/api/exams/{running}"));
        detail.GetProperty("durationMinutes").GetInt32().Should().Be(60);
        detail.GetProperty("status").GetString().Should().NotBe("Ended");
        (await _host.AuditLogsAsync("EXAM_TIME_EXTENDED")).Should().BeEmpty();
        (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The co-proctor still supervises (per-student extra time is a proctor action); the owner extends the exam.
        using (var hub = _host.CreateHub(TestHost.Principal(coId, "Teacher")))
            await hub.Hub.ExtendStudentTime(sid, 5);
        (await owner.PostAsJsonAsync($"/api/exams/{running}/extend-time", new { additionalMinutes = 30 }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Edit / delete a scheduled exam, or create one in someone else's section: owner or admin only.
        var scheduled = await _host.CreateExamAsync(Array.Empty<Guid>(), DateTime.UtcNow.AddDays(1));
        (await outsider.PutAsJsonAsync($"/api/exams/{scheduled}", ExamPayload(DateTime.UtcNow.AddDays(2))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.DeleteAsync($"/api/exams/{scheduled}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outsider.PostAsJsonAsync("/api/exams", ExamPayload(DateTime.UtcNow.AddDays(2))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync($"/api/exams/{scheduled}", ExamPayload(DateTime.UtcNow.AddDays(2))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.DeleteAsync($"/api/exams/{scheduled}")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Force-ending is the owner's call too.
        (await owner.PostAsync($"/api/exams/{running}/force-submit-all", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M10_I_16_OtherTeachers_SeeTheExam_ButNotItsAnswerKey()
    {
        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        var outsider = await _host.LoginAsync(email, password);

        var foreign = await TestHost.JsonAsync(await outsider.GetAsync($"/api/exams/{_host.Seed.ExamId}"));
        foreign.GetProperty("questions").EnumerateArray().Should().OnlyContain(q =>
            q.GetProperty("referenceAnswer").ValueKind == JsonValueKind.Null &&
            q.GetProperty("correctOptionIndex").ValueKind == JsonValueKind.Null &&
            q.GetProperty("expectedOutput").ValueKind == JsonValueKind.Null);

        var exam = await _host.CreateExamAsync(Array.Empty<Guid>());
        var own = await TestHost.JsonAsync(await (await _host.AsTeacher()).GetAsync($"/api/exams/{exam}"));
        own.GetProperty("questions")[0].GetProperty("referenceAnswer").GetString().Should().Be("Stack is LIFO, queue is FIFO.");
    }

    [Fact]
    public async Task M10_I_17_MarkAllRead_ClearsAWarning_AndIsAuditedAsAcknowledged()
    {
        // read-all also stops a warning reaching the exam window, so it must leave the same evidence as Acknowledge.
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.SendWarning(_host.Seed.StudentId.ToString(), "Phone away please");
        (await AckAsync(student, sid)).GetProperty("warnings").GetArrayLength().Should().Be(1);

        (await student.PatchAsync("/api/notifications/read-all", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AckAsync(student, sid)).GetProperty("warnings").GetArrayLength().Should().Be(0);
        var acknowledged = await _host.AuditLogsAsync("PROCTOR_WARNING_ACKNOWLEDGED");
        acknowledged.Should().ContainSingle();
    }
}
