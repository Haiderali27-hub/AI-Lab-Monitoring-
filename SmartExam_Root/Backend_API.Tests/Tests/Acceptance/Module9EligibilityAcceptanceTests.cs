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
/// Contract: IMPLEMENTATION_PLAN.md §3.3 — attendance CSV import, recalculate endpoint,
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

    [Fact]
    public async Task M9_U_01_Attendance30Of40_Is75Percent_Eligible()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 30)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = For(await RecalculateAndGetAssignmentsAsync(teacher), _host.Seed.StudentId);
        row.GetProperty("isEligible").GetBoolean().Should().BeTrue();
        row.GetProperty("attendancePercent").GetDouble().Should().Be(75.0);
    }

    [Fact]
    public async Task M9_U_02_ThresholdBoundary_74_9Ineligible_75Eligible()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 1000, 749), (SeededData.Student2Email, 1000, 750))))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await RecalculateAndGetAssignmentsAsync(teacher);
        For(rows, _host.Seed.StudentId).GetProperty("isEligible").GetBoolean().Should().BeFalse("74.9% < 75%");
        For(rows, _host.Seed.Student2Id).GetProperty("isEligible").GetBoolean().Should().BeTrue("75% meets the minimum");
    }

    [Fact]
    public async Task M9_U_03_ZeroClasses_NotComputed_AndFlagged()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 0, 0)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var row = For(await RecalculateAndGetAssignmentsAsync(teacher), _host.Seed.StudentId);
        row.GetProperty("isEligible").GetBoolean().Should().BeTrue("no division by zero — the existing decision is left as is");
        row.GetProperty("eligibilityNote").GetString().Should().NotBeNullOrWhiteSpace().And.ContainEquivalentOf("no attendance");
    }

    // ── I rows ───────────────────────────────────────────────────────────────

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task M9_I_05_SeatMatchByMachineName_Start200()
    {
        var student = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-01");
        (await _host.StartSessionRawAsync(student, _host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
    public async Task M9_I_08_NoSeatAssigned_NotEnforced_Start200()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });   // assignments created without a workstation
        var student = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "ANY-PC-42");

        (await _host.StartSessionRawAsync(student, examId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.AuditLogsAsync("SEAT_MISMATCH")).Should().BeEmpty();
    }

    [Fact]
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

    [Fact]
    public async Task M9_I_10_AutoRegisterOffByDefault_NoWorkstationCreatedOnLogin()
    {
        var admin = await _host.AsAdmin();
        (await admin.PostAsJsonAsync("/api/labs", new { name = "Systems Engineering Lab", location = "Block 2" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "NEW-PC-77");

        (await _host.WithDbAsync(db => db.Workstations.AnyAsync(w => w.MachineNumber == "NEW-PC-77"))).Should().BeFalse();
    }

    // ── Hardening beyond the catalogue's first ten rows (M9-I-11…18) ─────────

    [Fact]
    public async Task M9_I_11_Import_OtherTeachersSection403_BadHeader400_NotEnrolledIsWarning()
    {
        var (_, otherEmail, otherPassword) = await _host.CreateUserAsync("Teacher");
        var other = await _host.LoginAsync(otherEmail, otherPassword);
        (await ImportAttendanceAsync(other, Csv((SeededData.StudentEmail, 40, 30)))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.WithDbAsync(db => db.AttendanceRecords.CountAsync())).Should().Be(0);

        var teacher = await _host.AsTeacher();
        var bad = await ImportAttendanceAsync(teacher, "student,classes\nx,1\n");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // A student who isn't formally enrolled is still imported, with a warning.
        var (_, loneEmail, _) = await _host.CreateUserAsync("Student");
        var resp = await ImportAttendanceAsync(teacher, Csv((loneEmail, 10, 9), (SeededData.StudentEmail, 10, 11)));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHost.JsonAsync(resp);
        body.GetProperty("imported").GetInt32().Should().Be(1);
        body.GetProperty("warnings").EnumerateArray().Single().GetProperty("email").GetString().Should().Be(loneEmail);
        body.GetProperty("errors").EnumerateArray().Single().GetProperty("reason").GetString().Should().Contain("greater than total");

        // Re-import updates in place and is audited.
        var again = await TestHost.JsonAsync(await ImportAttendanceAsync(teacher, Csv((loneEmail, 12, 10))));
        again.GetProperty("updated").GetInt32().Should().Be(1);
        (await _host.WithDbAsync(db => db.AttendanceRecords.CountAsync())).Should().Be(1);
        (await _host.AuditLogsAsync("ATTENDANCE_IMPORTED")).Should().HaveCount(2);

        var rows = await (await teacher.GetAsync($"/api/sections/{_host.Seed.SectionId}/attendance")).Content.ReadFromJsonAsync<List<JsonElement>>();
        rows!.Single().GetProperty("attendedClasses").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task M9_I_12_Recalculate_EndedExam409_OtherTeacher403_Student403()
    {
        var (_, otherEmail, otherPassword) = await _host.CreateUserAsync("Teacher");
        (await (await _host.LoginAsync(otherEmail, otherPassword)).PostAsync($"/api/exams/{_host.Seed.ExamId}/eligibility/recalculate", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await _host.AsStudent()).PostAsync($"/api/exams/{_host.Seed.ExamId}/eligibility/recalculate", null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var teacher = await _host.AsTeacher();
        (await teacher.PostAsync($"/api/exams/{_host.Seed.ExamId}/force-submit-all", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.PostAsync($"/api/exams/{_host.Seed.ExamId}/eligibility/recalculate", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task M9_I_13_ClearOverride_HandsDecisionBackToAttendance_AndOnlyChangedRowsAreOverridden()
    {
        var teacher = await _host.AsTeacher();
        // The page sends EVERY row on save; only the one that changed becomes an override.
        (await teacher.PutAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/eligibility", new
        {
            assignments = new[]
            {
                new { userId = _host.Seed.StudentId, isEligible = true, eligibilityNote = "HOD approved", clearOverride = false },
                new { userId = _host.Seed.Student2Id, isEligible = true, eligibilityNote = "", clearOverride = false }
            }
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.AuditLogsAsync("ELIGIBILITY_OVERRIDDEN")).Should().ContainSingle();

        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 24), (SeededData.Student2Email, 40, 20)))).StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = await RecalculateAndGetAssignmentsAsync(teacher);
        For(rows, _host.Seed.StudentId).GetProperty("isEligible").GetBoolean().Should().BeTrue("manual override survives");
        For(rows, _host.Seed.Student2Id).GetProperty("isEligible").GetBoolean().Should().BeFalse("50% < 75%");
        For(rows, _host.Seed.Student2Id).GetProperty("eligibilitySource").GetString().Should().Be("Attendance");

        (await teacher.PutAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/eligibility", new
        {
            assignments = new[] { new { userId = _host.Seed.StudentId, isEligible = true, eligibilityNote = "HOD approved", clearOverride = true } }
        })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.AuditLogsAsync("ELIGIBILITY_OVERRIDE_CLEARED")).Should().ContainSingle();

        var after = For(await RecalculateAndGetAssignmentsAsync(teacher), _host.Seed.StudentId);
        after.GetProperty("isEligible").GetBoolean().Should().BeFalse();
        after.GetProperty("eligibilityOverridden").GetBoolean().Should().BeFalse();
        after.GetProperty("eligibilityNote").GetString().Should().Be("Attendance 60% < 75%");
    }

    [Fact]
    public async Task M9_I_14_HeartbeatMismatch_RecordedOncePerWindow_AndReportedOnTheTile()
    {
        var atSeat = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-01");
        var sid = await _host.StartSessionAsync(atSeat, _host.Seed.ExamId);
        await TestHost.HeartbeatAsync(atSeat, sid);
        _host.Hub.Named("StudentHeartbeat").Last().ArgsJson.Should().Contain("\"seatStatus\":\"Ok\"");

        var moved = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-07");
        for (var i = 0; i < 3; i++)
            (await TestHost.HeartbeatAsync(moved, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await _host.WithDbAsync(db => db.MonitoringEvents.CountAsync(e => e.ExamSessionId == sid && e.EventType == MonitoringEventType.SeatMismatch)))
            .Should().Be(1, "repeated heartbeats from the same wrong seat are de-duplicated");
        (await _host.AuditLogsAsync("SEAT_MISMATCH")).Should().ContainSingle();

        var beat = _host.Hub.Named("StudentHeartbeat").Last().ArgsJson;
        beat.Should().Contain("\"seatStatus\":\"Mismatch\"").And.Contain("PC-07").And.Contain("\"tileStatus\":\"Violation\"");
        _host.Hub.Named("ViolationEvent").Should().Contain(m => m.ArgsJson.Contains("SeatMismatch") && m.ArgsJson.Contains("logged in from PC-07"));
    }

    [Fact]
    public async Task M9_I_15_DesktopStartPath_AlsoEnforcesSeat_AndCurrentExamWarnsFirst()
    {
        var wrong = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-02");

        var status = await TestHost.DataAsync(await wrong.GetAsync("/api/exams/student/current"));
        status.GetProperty("seatStatus").GetString().Should().Be("Mismatch");
        status.GetProperty("canStartNow").GetBoolean().Should().BeFalse();
        status.GetProperty("message").GetString().Should().Contain("seat PC-01");

        var resp = await wrong.PostAsync("/api/exams/student/start", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("SEAT_MISMATCH");
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.UserId == _host.Seed.StudentId))).Should().Be(0);

        var right = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "pc-01");
        (await TestHost.DataAsync(await right.GetAsync("/api/exams/student/current"))).GetProperty("seatStatus").GetString().Should().Be("Ok");
        (await right.PostAsync("/api/exams/student/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M9_I_16_Workstations_EditHostNameDrivesMatching_DeleteRefusedWhileAssigned()
    {
        var admin = await _host.AsAdmin();
        var edit = await admin.PutAsJsonAsync($"/api/labs/{_host.Seed.LabId}/workstations/{_host.Seed.WorkstationId}",
            new { machineNumber = "PC-01", hostName = "LAB-PC-07", ipAddress = "192.168.1.1" });
        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.AuditLogsAsync("WORKSTATION_UPDATED")).Should().ContainSingle();
        (await admin.PutAsJsonAsync($"/api/labs/{_host.Seed.LabId}/workstations/{_host.Seed.WorkstationId}",
            new { machineNumber = "PC-01", ipAddress = "not-an-ip" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The seat label no longer matches; the real PC name does.
        var byLabel = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-01");
        (await _host.StartSessionRawAsync(byLabel, _host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var byHost = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "LAB-PC-07");
        (await _host.StartSessionRawAsync(byHost, _host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.DeleteAsync($"/api/labs/{_host.Seed.LabId}/workstations/{_host.Seed.WorkstationId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await (await _host.AsTeacher()).DeleteAsync($"/api/labs/{_host.Seed.LabId}/workstations/{_host.Seed.WorkstationId}"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var spare = await admin.PostAsJsonAsync($"/api/labs/{_host.Seed.LabId}/workstations", new { machineNumber = "PC-50", ipAddress = "192.168.1.50" });
        var spareId = (await TestHost.JsonAsync(spare)).GetProperty("workstationId").GetGuid();
        (await admin.DeleteAsync($"/api/labs/{_host.Seed.LabId}/workstations/{spareId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M9_I_17_AutoRegisterOptIn_And_UnregisteredMachinesList()
    {
        // Default: the unknown PC is listed for an admin to register, not auto-created.
        await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "NEW-PC-77");
        var list = await (await (await _host.AsAdmin()).GetAsync("/api/labs/unregistered-machines")).Content.ReadFromJsonAsync<List<JsonElement>>();
        list!.Should().Contain(m => m.GetProperty("machineName").GetString() == "NEW-PC-77");

        // Demo opt-in (Labs:AutoRegister=true) keeps the old behaviour.
        using var demo = new TestHost(b => b.UseSetting("Labs:AutoRegister", "true"));
        (await (await demo.AsAdmin()).PostAsJsonAsync("/api/labs", new { name = "Systems Engineering Lab", location = "Block 2" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await demo.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "NEW-PC-77");
        (await demo.WithDbAsync(db => db.Workstations.AnyAsync(w => w.MachineNumber == "NEW-PC-77"))).Should().BeTrue();
    }

    [Fact]
    public async Task M9_I_18_EnforceSeatOff_And_RequireDesktopClient()
    {
        // EnforceSeat=false: a seat may be assigned for the map without blocking anyone.
        var teacher = await _host.AsTeacher();
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, extra: new Dictionary<string, object?> { ["enforceSeat"] = false });
        (await teacher.PutAsJsonAsync($"/api/exams/{examId}/assignments/{_host.Seed.StudentId}/workstation",
            new { workstationId = _host.Seed.WorkstationId })).StatusCode.Should().Be(HttpStatusCode.OK);
        var elsewhere = await _host.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "HOME-PC");
        (await _host.StartSessionRawAsync(elsewhere, examId)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Exams:RequireDesktopClientForStart=true: web-portal tokens can't start an exam at all.
        using var strict = new TestHost(b => b.UseSetting("Exams:RequireDesktopClientForStart", "true"));
        var web = await strict.AsStudent();
        var refused = await strict.StartSessionRawAsync(web, strict.Seed.ExamId);
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(refused)).Should().Be("DESKTOP_CLIENT_REQUIRED");
        var desktop = await strict.StudentClientLoginAsync(SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid, machineName: "PC-01");
        (await strict.StartSessionRawAsync(desktop, strict.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Verifier regressions (M9-I-19, M9-I-20) ──────────────────────────────

    [Fact]
    public async Task M9_I_19_OtherTeacher_CannotReadOrChange_RosterEligibilityOrSeats()
    {
        var teacher = await _host.AsTeacher();
        (await ImportAttendanceAsync(teacher, Csv((SeededData.StudentEmail, 40, 30)))).StatusCode.Should().Be(HttpStatusCode.OK);

        var (_, otherEmail, otherPassword) = await _host.CreateUserAsync("Teacher");
        var other = await _host.LoginAsync(otherEmail, otherPassword);
        var exam = _host.Seed.ExamId;
        var student = _host.Seed.StudentId;

        // The assignment list carries the section's attendance — another teacher must not see it.
        (await other.GetAsync($"/api/exams/{exam}/assignments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PutAsJsonAsync($"/api/exams/{exam}/eligibility", new
        {
            assignments = new[] { new { userId = student, isEligible = false, eligibilityNote = "not mine to decide" } }
        })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PutAsJsonAsync($"/api/exams/{exam}/assignments/{student}/workstation", new { workstationId = (Guid?)null }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.DeleteAsync($"/api/exams/{exam}/assignments/{_host.Seed.Student2Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var (loneId, _, _) = await _host.CreateUserAsync("Student");
        (await other.PostAsJsonAsync($"/api/exams/{exam}/assignments", new { userId = loneId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Nothing changed, nothing audited on the other teacher's behalf.
        var row = await _host.WithDbAsync(db => db.ExamAssignments.SingleAsync(a => a.ExamId == exam && a.UserId == student));
        row.IsEligible.Should().BeTrue();
        row.WorkstationId.Should().Be(_host.Seed.WorkstationId);
        (await _host.AuditLogsAsync("ELIGIBILITY_UPDATED")).Should().BeEmpty();
        (await _host.WithDbAsync(db => db.ExamAssignments.CountAsync(a => a.ExamId == exam && a.UserId == loneId))).Should().Be(0);

        // The owner and an admin still can; an unknown exam is 404 rather than an empty 200.
        (await teacher.GetAsync($"/api/exams/{exam}/assignments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _host.AsAdmin()).GetAsync($"/api/exams/{exam}/assignments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.GetAsync($"/api/exams/{Guid.NewGuid()}/assignments")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task M9_I_20_IpMode_AlsoChecksWebPortalTokens_ByRequestIp()
    {
        using var host = new TestHost(b => b.UseSetting("SeatMatch", "Ip"));

        // A web-portal token has no machine name, but the server sees the request IP: a wrong IP is refused.
        var wrong = (await host.AsStudent()).FromIp("10.0.0.99");
        var status = await TestHost.DataAsync(await wrong.GetAsync("/api/exams/student/current"));
        status.GetProperty("seatStatus").GetString().Should().Be("Mismatch");
        var resp = await host.StartSessionRawAsync(wrong, host.Seed.ExamId);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("SEAT_MISMATCH");
        (await host.AuditLogsAsync("SEAT_MISMATCH")).Should().ContainSingle();

        var right = (await host.AsStudent()).FromIp("192.168.1.1");
        (await host.StartSessionRawAsync(right, host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.OK);

        // MachineName mode (the default) cannot check a web token at all — it is reported, not blocked.
        var web = await _host.AsStudent();
        (await TestHost.DataAsync(await web.GetAsync("/api/exams/student/current"))).GetProperty("seatStatus").GetString().Should().Be("NotChecked");
    }
}
