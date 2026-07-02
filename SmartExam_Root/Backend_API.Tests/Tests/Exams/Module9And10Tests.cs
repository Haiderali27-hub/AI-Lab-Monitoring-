using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Exams;

/// <summary>
/// Module 9 (Eligibility &amp; Access Control: labs, seating map, roster changes)
/// + Module 10 (Live Proctoring: time extension, heartbeat clock resync).
/// Fresh factory per test for isolation.
/// </summary>
public class Module9And10Tests : IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public Module9And10Tests()
    {
        _factory = new TestWebAppFactory();
        _client = _factory.CreateClient();
        _seed = _factory.SeededData;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ══════════════ MODULE 9 — Labs & workstations ══════════════

    [Fact]
    public async Task GetLabs_AsTeacher_ReturnsLabsWithWorkstations()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var resp = await _client.GetAsync("/api/labs");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var labs = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        labs!.Should().NotBeEmpty();
        labs![0].GetProperty("workstations").EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateLabAndWorkstation_AsAdmin_Works()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);

        var labResp = await _client.PostAsJsonAsync("/api/labs", new { name = "Lab Z", location = "Block 9" });
        labResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var labId = (await labResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("labId").GetString();

        var wsResp = await _client.PostAsJsonAsync($"/api/labs/{labId}/workstations", new { machineNumber = "PC-99", ipAddress = "10.0.0.99" });
        wsResp.StatusCode.Should().Be(HttpStatusCode.Created);

        // Duplicate machine number in the same lab is rejected
        var dupResp = await _client.PostAsJsonAsync($"/api/labs/{labId}/workstations", new { machineNumber = "PC-99", ipAddress = "10.0.0.100" });
        dupResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateLab_AsTeacher_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var resp = await _client.PostAsJsonAsync("/api/labs", new { name = "Rogue Lab", location = "X" });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ══════════════ MODULE 9 — Seating map ══════════════

    [Fact]
    public async Task AssignWorkstation_SeatConflict_Returns409()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);

        // Seeder gives BOTH students the same workstation; re-assigning it to student1
        // must trip the conflict check because student2 already holds that seat.
        var resp = await _client.PutAsJsonAsync(
            $"/api/exams/{_seed.ExamId}/assignments/{_seed.StudentId}/workstation",
            new { workstationId = _seed.WorkstationId });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AssignWorkstation_FreshSeat_AndUnassign_Work()
    {
        // Admin creates a new seat
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var wsResp = await _client.PostAsJsonAsync($"/api/labs/{_seed.LabId}/workstations", new { machineNumber = "PC-02", ipAddress = "192.168.1.2" });
        var wsId = (await wsResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("workstationId").GetString();

        // Teacher seats student1 there
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var assignResp = await _client.PutAsJsonAsync(
            $"/api/exams/{_seed.ExamId}/assignments/{_seed.StudentId}/workstation",
            new { workstationId = wsId });
        assignResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var assignments = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}/assignments")).Content.ReadFromJsonAsync<List<JsonElement>>();
        var mine = assignments!.First(a => a.GetProperty("userId").GetString() == _seed.StudentId.ToString());
        mine.GetProperty("workstationNumber").GetString().Should().Be("PC-02");

        // Unassign (null) clears the seat
        var clearResp = await _client.PutAsJsonAsync(
            $"/api/exams/{_seed.ExamId}/assignments/{_seed.StudentId}/workstation",
            new { workstationId = (string?)null });
        clearResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AssignWorkstation_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var resp = await _client.PutAsJsonAsync(
            $"/api/exams/{_seed.ExamId}/assignments/{_seed.StudentId}/workstation",
            new { workstationId = _seed.WorkstationId });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ══════════════ MODULE 9 — Roster changes on an existing exam ══════════════

    [Fact]
    public async Task AddStudentToExistingExam_ThenDuplicate_Returns200Then409()
    {
        // Admin creates a fresh student
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var email = $"late_{Guid.NewGuid()}@test.com";
        var created = await _client.PostAsJsonAsync("/api/users", new { name = "Late Joiner", email, password = "Pass@123", role = "Student" });
        var newStudentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString();

        // Teacher adds them to the seeded exam
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var addResp = await _client.PostAsJsonAsync($"/api/exams/{_seed.ExamId}/assignments", new { userId = newStudentId });
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Adding twice conflicts
        var dupResp = await _client.PostAsJsonAsync($"/api/exams/{_seed.ExamId}/assignments", new { userId = newStudentId });
        dupResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task AddNonStudentToExam_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var resp = await _client.PostAsJsonAsync($"/api/exams/{_seed.ExamId}/assignments", new { userId = _seed.TeacherId });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveStudent_WithoutSession_Works()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var resp = await _client.DeleteAsync($"/api/exams/{_seed.ExamId}/assignments/{_seed.Student2Id}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var assignments = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}/assignments")).Content.ReadFromJsonAsync<List<JsonElement>>();
        assignments!.Any(a => a.GetProperty("userId").GetString() == _seed.Student2Id.ToString()).Should().BeFalse();
    }

    [Fact]
    public async Task RemoveStudent_WithSession_Returns409()
    {
        // Student starts a session first
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        (await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Teacher can no longer unassign them — history must be preserved
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var resp = await _client.DeleteAsync($"/api/exams/{_seed.ExamId}/assignments/{_seed.StudentId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ══════════════ MODULE 10 — Extend time + heartbeat clock ══════════════

    [Fact]
    public async Task ExtendTime_IncreasesDuration_AndStudentClock()
    {
        // Baseline remaining seconds from the student dashboard
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var before = await (await _client.GetAsync("/api/exams/student/current")).Content.ReadFromJsonAsync<JsonElement>();
        var beforeSeconds = before.GetProperty("data").GetProperty("remainingSeconds").GetInt32();

        // Teacher grants +30 minutes
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var extendResp = await _client.PostAsJsonAsync($"/api/exams/{_seed.ExamId}/extend-time", new { additionalMinutes = 30 });
        extendResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await extendResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("durationMinutes").GetInt32().Should().Be(90); // 60 + 30

        // Student clock grew by ~30 minutes
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var after = await (await _client.GetAsync("/api/exams/student/current")).Content.ReadFromJsonAsync<JsonElement>();
        var afterSeconds = after.GetProperty("data").GetProperty("remainingSeconds").GetInt32();
        (afterSeconds - beforeSeconds).Should().BeGreaterThan(1700); // ~1800s minus test latency
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(500)]
    public async Task ExtendTime_InvalidMinutes_Returns400(int minutes)
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var resp = await _client.PostAsJsonAsync($"/api/exams/{_seed.ExamId}/extend-time", new { additionalMinutes = minutes });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ExtendTime_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var resp = await _client.PostAsJsonAsync($"/api/exams/{_seed.ExamId}/extend-time", new { additionalMinutes = 15 });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Heartbeat_WithOwnedSession_ReturnsRemainingSeconds()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString();

        var hbResp = await _client.PostAsJsonAsync("/api/monitoring/heartbeat", new
        {
            examSessionId = sessionId,
            isForegroundExamApp = true,
            activeWindowTitle = "SmartExam Dashboard",
            processListSnapshot = "code,chrome"
        });
        hbResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var ack = (await hbResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        ack.GetProperty("remainingSeconds").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ViolationEvent_FocusLoss_IsPersisted()
    {
        // This is the client's focus-loss violation path end-to-end at the API level
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString();

        var evResp = await _client.PostAsJsonAsync("/api/monitoring/event", new
        {
            eventType = "Violation",
            examSessionId = sessionId,
            payloadJson = "{\"reason\":\"FocusLoss\",\"activeWindow\":\"YouTube - Chrome\"}"
        });
        evResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // The violation now shows up in the heartbeat's violation count
        var hbResp = await _client.PostAsJsonAsync("/api/monitoring/heartbeat", new
        {
            examSessionId = sessionId,
            isForegroundExamApp = true,
            activeWindowTitle = "SmartExam Dashboard",
            processListSnapshot = ""
        });
        hbResp.StatusCode.Should().Be(HttpStatusCode.OK);
        // (Count is broadcast to proctors; persistence verified by the student violations endpoint)
        var violations = await (await _client.GetAsync("/api/student/violations")).Content.ReadFromJsonAsync<JsonElement>();
        violations.ToString().Should().Contain("FocusLoss");
    }
}
