using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M4 — Secure Exam Environment, backend side (TEST_CASES.md §M4, layer I).
/// Planned contract: IMPLEMENTATION_PLAN.md §3.1 — Exam.LockdownMode, extended MonitoringEventType,
/// server-side ViolationClassifier driving the live-monitor tileStatus.
/// </summary>
public class Module4LockdownAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task M4_I_01_StudentCurrentExam_ReturnsLockdownModeAndAllowedApps()
    {
        var (id, email, password) = await _host.CreateUserAsync("Student");
        await _host.CreateExamAsync(new[] { id }, extra: new Dictionary<string, object?>
        {
            ["lockdownMode"] = "Enforce",
            ["blockMultiMonitor"] = true,
            ["allowedApps"] = "[\"code.exe\",\"codeblocks.exe\"]"
        });

        var student = await _host.LoginAsync(email, password);
        var resp = await student.GetAsync("/api/exams/student/current");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await TestHost.DataAsync(resp);

        data.GetProperty("lockdownMode").GetString().Should().Be("Enforce");
        data.GetProperty("allowedApps").GetString().Should().Contain("code.exe").And.Contain("codeblocks.exe");
    }

    [Fact]
    public async Task M4_I_02_UnauthorizedProcessEvent_Returns200_AndStoredWithType()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        var resp = await TestHost.PostEventAsync(student, sid, "UnauthorizedProcess", "{\"process\":\"chrome.exe\",\"pid\":4242}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await _host.WithDbAsync(db => db.MonitoringEvents.Where(e => e.ExamSessionId == sid).ToListAsync());
        stored.Should().ContainSingle(e => e.EventType.ToString() == "UnauthorizedProcess" && e.Payload.Contains("chrome.exe"));
    }

    [Fact]
    public async Task M4_I_03_SeverityClassification_DrivesTileStatus()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        async Task<string> TileStatusAfterHeartbeat()
        {
            (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
            var last = _host.Hub.Named("StudentHeartbeat").Last();
            last.Target.Should().Be($"exam:{_host.Seed.ExamId}");
            return JsonDocument.Parse(last.ArgsJson).RootElement[0].GetProperty("tileStatus").GetString()!;
        }

        (await TileStatusAfterHeartbeat()).Should().Be("Normal");

        (await TestHost.PostEventAsync(student, sid, "FocusLost")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TileStatusAfterHeartbeat()).Should().Be("Warning", "1 FocusLost → Warning");

        (await TestHost.PostEventAsync(student, sid, "FocusLost")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.PostEventAsync(student, sid, "FocusLost")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TileStatusAfterHeartbeat()).Should().Be("Suspicious", "3 FocusLost → Suspicious");

        (await TestHost.PostEventAsync(student, sid, "ProcessKilled", "{\"process\":\"chrome.exe\"}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TileStatusAfterHeartbeat()).Should().Be("Violation", "ProcessKilled → Violation");
    }

    [Fact]
    public async Task M4_I_04_ViolationOnOtherStudentsSession_Returns404_AndNothingStored()
    {
        var victim = await _host.AsStudent();
        var victimSession = await _host.StartSessionAsync(victim, _host.Seed.ExamId);

        var attacker = await _host.AsStudent2();
        await _host.StartSessionAsync(attacker, _host.Seed.ExamId);

        // Client monitoring endpoint
        (await TestHost.PostEventAsync(attacker, victimSession, "Violation", "{\"framed\":true}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Legacy per-session endpoint
        (await attacker.PostAsJsonAsync($"/api/exams/sessions/{victimSession}/monitoring-event",
            new { eventType = "Violation", payload = "{\"framed\":true}" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Heartbeats into a foreign session are not attributed to it
        (await TestHost.HeartbeatAsync(attacker, victimSession)).StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await _host.WithDbAsync(db => db.MonitoringEvents.Where(e => e.ExamSessionId == victimSession).ToListAsync());
        events.Should().BeEmpty();
        _host.Hub.Named("ViolationEvent").Should().BeEmpty();
    }

    [Fact]
    public async Task M4_I_05_PostedViolation_IsListedInStudentViolations()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var marker = $"chrome-{Guid.NewGuid():N}";

        (await TestHost.PostEventAsync(student, sid, "Violation", $"{{\"blockedApp\":\"{marker}\"}}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await (await student.GetAsync("/api/student/violations")).Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalViolations").GetInt32().Should().Be(1);
        var v = body.GetProperty("violations").EnumerateArray().Single();
        v.GetProperty("payload").GetString().Should().Contain(marker);
        v.GetProperty("examTitle").GetString().Should().Be("Test Exam");

        // Broadcast to the exam's proctor group only.
        _host.Hub.Named("ViolationEvent").Should().ContainSingle().Which.Target.Should().Be($"exam:{_host.Seed.ExamId}");

        // Another student does not see it.
        var other = await (await (await _host.AsStudent2()).GetAsync("/api/student/violations")).Content.ReadFromJsonAsync<JsonElement>();
        other.GetProperty("totalViolations").GetInt32().Should().Be(0);
    }
}
