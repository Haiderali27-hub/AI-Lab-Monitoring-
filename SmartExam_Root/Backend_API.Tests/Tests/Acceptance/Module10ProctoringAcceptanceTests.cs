using System.Net;
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
/// Planned contract: IMPLEMENTATION_PLAN.md §4.3 — POST/PATCH /api/incidents, hub UnlockSession /
/// ExtendStudentTime, ExamProctors (POST /api/exams/{id}/proctors).
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

    [Fact(Skip = "Pending: M10-I-01 — incidents")]
    public async Task M10_I_01_CreateIncident_Returns201_AndAudited()
    {
        var sid = await StudentSessionAsync();
        var teacher = await _host.AsTeacher();

        var id = await CreateIncidentAsync(teacher, sid);
        id.Should().NotBeEmpty();

        var logs = await _host.AuditLogsAsync("INCIDENT_CREATED");
        logs.Should().ContainSingle().Which.GetProperty("entityId").GetString().Should().Be(id.ToString());
    }

    [Fact(Skip = "Pending: M10-I-02 — incident status workflow")]
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

    [Fact(Skip = "Pending: M10-I-03 — incident access control")]
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

    [Fact(Skip = "Pending: M10-I-04 — hub UnlockSession")]
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
}
