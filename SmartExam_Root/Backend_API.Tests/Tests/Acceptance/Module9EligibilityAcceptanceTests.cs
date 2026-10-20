using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M9 — Eligibility &amp; Access Control (TEST_CASES.md §M9, layers U (via API) and I).
/// M9-A-01 (manual toggle blocks start) is covered here as M3_I_04 and in Playwright.
/// Planned contract: IMPLEMENTATION_PLAN.md §3.3 — attendance CSV import, recalculate endpoint,
/// EligibilityOverridden, seat enforcement (403 SEAT_MISMATCH), Labs:AutoRegister=false by default.
/// The seeded student is assigned to workstation PC-01 / 192.168.1.1 on the seeded exam.
/// </summary>
public class Module9EligibilityAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private Task<HttpResponseMessage> ImportAttendanceAsync(HttpClient teacher, string csv, Guid? sectionId = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "attendance.csv");
        return teacher.PostAsync($"/api/sections/{sectionId ?? _host.Seed.SectionId}/attendance/import", form);
    }

    private async Task<List<JsonElement>> RecalculateAndGetAssignmentsAsync(HttpClient teacher, Guid? examId = null)
    {
        var exam = examId ?? _host.Seed.ExamId;
        (await teacher.PostAsync($"/api/exams/{exam}/eligibility/recalculate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        return (await (await teacher.GetAsync($"/api/exams/{exam}/assignments")).Content.ReadFromJsonAsync<List<JsonElement>>())!;
    }

    private static JsonElement For(List<JsonElement> rows, Guid userId) => rows.Single(r => r.GetProperty("userId").GetGuid() == userId);

    private string Csv(params (string email, int total, int attended)[] rows) =>
        "email,total,attended\n" + string.Join("\n", rows.Select(r => $"{r.email},{r.total},{r.attended}"));

    // ── U rows expressed through the API ─────────────────────────────────────

    [Fact(Skip = "Pending: M9-U-01 — attendance percentage calculation")]
    public async Task M9_U_01_Attendance30Of40_Is75Percent_Eligible()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 30)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = For(await RecalculateAndGetAssignmentsAsync(teacher), _host.Seed.StudentId);
        row.GetProperty("isEligible").GetBoolean().Should().BeTrue();
        row.GetProperty("attendancePercent").GetDouble().Should().Be(75.0);
    }

    [Fact(Skip = "Pending: M9-U-02 — 75% threshold boundary")]
    public async Task M9_U_02_ThresholdBoundary_74_9Ineligible_75Eligible()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 1000, 749), (SeededData.Student2Email, 1000, 750))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await RecalculateAndGetAssignmentsAsync(teacher);
        For(rows, _host.Seed.StudentId).GetProperty("isEligible").GetBoolean().Should().BeFalse("74.9% < 75%");
        For(rows, _host.Seed.Student2Id).GetProperty("isEligible").GetBoolean().Should().BeTrue("75% meets the minimum");
    }

    [Fact(Skip = "Pending: M9-U-03 — zero classes is not computed")]
    public async Task M9_U_03_ZeroClasses_NotComputed_AndFlagged()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 0, 0)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = For(await RecalculateAndGetAssignmentsAsync(teacher), _host.Seed.StudentId);
        row.GetProperty("isEligible").GetBoolean().Should().BeTrue("no division by zero — the existing decision is left as is");
        row.GetProperty("eligibilityNote").GetString().Should().NotBeNullOrWhiteSpace().And.ContainEquivalentOf("no attendance");
    }

    // ── I rows ───────────────────────────────────────────────────────────────

    [Fact(Skip = "Pending: M9-I-01 — attendance CSV import")]
    public async Task M9_I_01_AttendanceCsvImport_StoresRecords()
    {
        var teacher = await _host.AsTeacher();
        var resp = await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 30), (SeededData.Student2Email, 40, 24)));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var records = await _host.WithDbAsync(db => Task.FromResult(Planned.Set(db, "AttendanceRecords").ToList()));
        records.Should().HaveCount(2);
        records.Select(r => (int)Planned.Get(r, "AttendedClasses")!).Should().BeEquivalentTo(new[] { 30, 24 });

        // Unknown emails are reported, not silently dropped; students can't import.
        var bad = await ImportAttendanceAsync(teacher, Csv(("ghost@test.com", 10, 5)));
        (await bad.Content.ReadAsStringAsync()).Should().Contain("ghost@test.com");
        (await ImportAttendanceAsync(await _host.AsStudent(), Csv((SeededData.StudentEmail, 1, 1))))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(Skip = "Pending: M9-I-02 — recalculate eligibility from attendance")]
    public async Task M9_I_02_Recalculate_60PercentWithMin75_SetsIneligible_WithNote()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 24)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = For(await RecalculateAndGetAssignmentsAsync(teacher), _host.Seed.StudentId);
        row.GetProperty("isEligible").GetBoolean().Should().BeFalse();
        row.GetProperty("eligibilityNote").GetString().Should().Be("Attendance 60% < 75%");

        var resp = await _host.StartSessionRawAsync(await _host.AsStudent(), _host.Seed.ExamId);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(Skip = "Pending: M9-I-03 — manual overrides survive recalculation")]
    public async Task M9_I_03_Recalculate_KeepsManualOverride()
    {
        var teacher = await _host.AsTeacher();
        (await teacher.PutAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/eligibility", new
        {
            assignments = new[] { new { userId = _host.Seed.StudentId, isEligible = true, eligibilityNote = "Medical leave — HOD approved" } }
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 24)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = For(await RecalculateAndGetAssignmentsAsync(teacher), _host.Seed.StudentId);
        row.GetProperty("isEligible").GetBoolean().Should().BeTrue();
        row.GetProperty("eligibilityOverridden").GetBoolean().Should().BeTrue();
        row.GetProperty("eligibilitySource").GetString().Should().Be("Manual");

        (await _host.AuditLogsAsync("ELIGIBILITY_OVERRIDDEN")).Should().NotBeEmpty();
    }

    [Fact(Skip = "Pending: M9-I-04 — eligibility decisions audited")]
    public async Task M9_I_04_Recalculate_AuditsEligibilityComputedPerStudent()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 24), (SeededData.Student2Email, 40, 40))))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        await RecalculateAndGetAssignmentsAsync(teacher);

        var logs = await _host.AuditLogsAsync("ELIGIBILITY_COMPUTED");
        logs.Should().HaveCount(2);
        string.Join("|", logs.Select(l => l.GetRawText())).Should().Contain(_host.Seed.StudentId.ToString()).And.Contain(_host.Seed.Student2Id.ToString());
    }

    [Fact(Skip = "Pending: M9-I-05 — seat enforcement (match by machine name)")]
    public async Task M9_I_05_SeatMatchByMachineName_Start200()
    {
        var student = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-01");
        (await _host.StartSessionRawAsync(student, _host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Skip = "Pending: M9-I-06 — seat enforcement (mismatch)")]
    public async Task M9_I_06_SeatMismatch_Returns403_AndAudited()
    {
        var student = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-02");

        var resp = await _host.StartSessionRawAsync(student, _host.Seed.ExamId);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("SEAT_MISMATCH");

        (await _host.AuditLogsAsync("SEAT_MISMATCH")).Should().ContainSingle();
        _host.Hub.Messages.Should().Contain(m => m.Target == $"exam:{_host.Seed.ExamId}" && m.ArgsJson.Contains("SEAT_MISMATCH", StringComparison.OrdinalIgnoreCase));
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.UserId == _host.Seed.StudentId))).Should().Be(0);
    }

    [Fact(Skip = "Pending: M9-I-07 — seat enforcement (SeatMatch=Ip)")]
    public async Task M9_I_07_SeatMatchByIp_ComparesRequestIp()
    {
        using var host = new TestHost(b => b.UseSetting("SeatMatch", "Ip"));

        // Right IP, wrong machine name → allowed in Ip mode.
        var ok = (await host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "RENAMED-PC"))
            .FromIp("192.168.1.1");
        (await host.StartSessionRawAsync(ok, host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Student2 is assigned PC-01 too, but connects from another IP → blocked even with the right machine name.
        var wrong = (await host.StudentClientLoginAsync(SeededData.Student2Email, SeededData.Student2Password, SeededData.TestHwid2, machineName: "PC-01"))
            .FromIp("10.0.0.99");
        var resp = await host.StartSessionRawAsync(wrong, host.Seed.ExamId);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("SEAT_MISMATCH");
    }

    [Fact(Skip = "Pending: M9-I-08 — seat enforcement skipped when no seat assigned")]
    public async Task M9_I_08_NoSeatAssigned_NotEnforced_Start200()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });   // assignments created without a workstation
        var student = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "ANY-PC-42");

        (await _host.StartSessionRawAsync(student, examId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.AuditLogsAsync("SEAT_MISMATCH")).Should().BeEmpty();
    }

    [Fact(Skip = "Pending: M9-I-09 — seat re-checked on every heartbeat")]
    public async Task M9_I_09_HeartbeatFromWrongSeat_RecordsSeatMismatchViolation()
    {
        var atSeat = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-01");
        var sid = await _host.StartSessionAsync(atSeat, _host.Seed.ExamId);

        // Mid-exam the same (bound) student shows up from another seat.
        var moved = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-07");
        await TestHost.HeartbeatAsync(moved, sid);

        var events = await _host.WithDbAsync(db => db.MonitoringEvents.Where(e => e.ExamSessionId == sid).ToListAsync());
        events.Should().Contain(e => e.EventType.ToString() == "SeatMismatch"
                                     || (e.EventType == MonitoringEventType.Violation && e.Payload.Contains("SeatMismatch")));
    }

    [Fact(Skip = "Pending: M9-I-10 — Labs:AutoRegister defaults to false")]
    public async Task M9_I_10_AutoRegisterOffByDefault_NoWorkstationCreatedOnLogin()
    {
        var admin = await _host.AsAdmin();
        (await admin.PostAsJsonAsync("/api/labs", new { name = "Systems Engineering Lab", location = "Block 2" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "NEW-PC-77");

        (await _host.WithDbAsync(db => db.Workstations.AnyAsync(w => w.MachineNumber == "NEW-PC-77"))).Should().BeFalse();
    }
}
