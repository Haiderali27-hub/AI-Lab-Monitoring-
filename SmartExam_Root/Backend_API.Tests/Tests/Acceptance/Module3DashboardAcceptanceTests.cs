using System.Net;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M3 — Student Exam Dashboard (TEST_CASES.md §M3, layer I).
/// Contract: IMPLEMENTATION_PLAN.md §3.4 — start-session / student/start before StartTime − Exam.EarlyJoinMinutes
/// → 400 EXAM_NOT_STARTED; student/current reports the gate on the server clock.
/// </summary>
public class Module3DashboardAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task M3_I_01_StartBeforeStartTime_Returns400_ExamNotStarted()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddHours(1));
        var student = await _host.AsStudent();

        var resp = await _host.StartSessionRawAsync(student, examId);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("EXAM_NOT_STARTED");
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.ExamId == examId))).Should().Be(0);
    }

    [Fact]
    public async Task M3_I_02_EarlyJoinWindow_StartAtTMinus3_With5MinWindow_Returns200()
    {
        var student = await _host.AsStudent();

        var inside = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddMinutes(3),
            extra: new Dictionary<string, object?> { ["earlyJoinMinutes"] = 5 });
        (await _host.StartSessionRawAsync(student, inside)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Same offset without an early-join window is still rejected.
        var outside = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddMinutes(3),
            extra: new Dictionary<string, object?> { ["earlyJoinMinutes"] = 0 });
        var resp = await _host.StartSessionRawAsync(student, outside);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("EXAM_NOT_STARTED");
    }

    [Fact]
    public async Task M3_I_03_StartAfterExamEnded_Returns400_AndCreatesNoSession()
    {
        await _host.EndExamWindowAsync(_host.Seed.ExamId, minutesAgo: 10);   // well past end + 2 min grace
        var student = await _host.AsStudent();

        var resp = await _host.StartSessionRawAsync(student, _host.Seed.ExamId);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.ExamId == _host.Seed.ExamId))).Should().Be(0);
    }

    [Fact]
    public async Task M3_I_03b_StartAfterTeacherEndedExam_Returns400()
    {
        // Ended by the teacher (force-submit-all) while still inside the scheduled window.
        var teacher = await _host.AsTeacher();
        (await teacher.PostAsync($"/api/exams/{_host.Seed.ExamId}/force-submit-all", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var student = await _host.AsStudent();
        (await _host.StartSessionRawAsync(student, _host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task M3_I_04_IneligibleStudent_StartReturns400_AndCreatesNoSession()
    {
        var teacher = await _host.AsTeacher();
        (await teacher.PutAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/eligibility", new
        {
            assignments = new[] { new { userId = _host.Seed.StudentId, isEligible = false, eligibilityNote = "Attendance shortfall" } }
        })).StatusCode.Should().Be(HttpStatusCode.OK);

        var student = await _host.AsStudent();
        var resp = await _host.StartSessionRawAsync(student, _host.Seed.ExamId);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.UserId == _host.Seed.StudentId))).Should().Be(0);

        // The other (still eligible) student is unaffected.
        var other = await _host.AsStudent2();
        (await _host.StartSessionRawAsync(other, _host.Seed.ExamId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Start gate details (M3-I-05…07) ──────────────────────────────────────

    /// A brand-new student assigned ONLY to <paramref name="startUtc"/>'s exam, so student/current
    /// and student/start pick that exam (the seeded student also has the seeded, already-open exam).
    private async Task<(HttpClient Client, Guid ExamId)> LoneStudentWithExamAsync(DateTime startUtc, int earlyJoinMinutes = 0, int durationMinutes = 60)
    {
        var (id, email, password) = await _host.CreateUserAsync("Student");
        var examId = await _host.CreateExamAsync(new[] { id }, startUtc: startUtc, durationMinutes: durationMinutes,
            extra: new Dictionary<string, object?> { ["earlyJoinMinutes"] = earlyJoinMinutes });
        return (await _host.LoginAsync(email, password), examId);
    }

    [Fact]
    public async Task M3_I_05_DesktopStart_BeforeOpen_ExamNotStarted_AfterEnd_ExamClosed()
    {
        var (early, earlyExam) = await LoneStudentWithExamAsync(DateTime.UtcNow.AddMinutes(30));
        var resp = await early.PostAsync("/api/exams/student/start", null);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("EXAM_NOT_STARTED");
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.ExamId == earlyExam))).Should().Be(0);

        // Inside the early-join window the desktop path starts too.
        var (joiner, _) = await LoneStudentWithExamAsync(DateTime.UtcNow.AddMinutes(3), earlyJoinMinutes: 5);
        (await joiner.PostAsync("/api/exams/student/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Window over (status never flipped to Ended) → EXAM_CLOSED, not the old catch-all OUTSIDE_EXAM_WINDOW.
        var (late, lateExam) = await LoneStudentWithExamAsync(DateTime.UtcNow.AddHours(-2), durationMinutes: 60);
        var closed = await late.PostAsync("/api/exams/student/start", null);
        closed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(closed)).Should().Be("EXAM_CLOSED");
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.ExamId == lateExam))).Should().Be(0);
    }

    [Fact]
    public async Task M3_I_06_CurrentExam_ReportsServerSideStartGate()
    {
        var (student, examId) = await LoneStudentWithExamAsync(DateTime.UtcNow.AddMinutes(20), earlyJoinMinutes: 5);
        var status = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));

        status.GetProperty("examId").GetGuid().Should().Be(examId);
        status.GetProperty("canStartNow").GetBoolean().Should().BeFalse();
        status.GetProperty("earlyJoinMinutes").GetInt32().Should().Be(5);
        // opens at T-5 → about 15 minutes from now, measured on the server clock
        status.GetProperty("secondsUntilStart").GetInt32().Should().BeInRange(14 * 60, 15 * 60 + 5);
        var opensAt = status.GetProperty("opensAtUtc").GetDateTime();
        var serverNow = status.GetProperty("serverTimeUtc").GetDateTime();
        (opensAt - serverNow).TotalMinutes.Should().BeApproximately(15, 0.2);
        status.GetProperty("message").GetString().Should().Be("Exam has not started yet.");

        // An open exam: can start now, nothing left to wait for.
        var (open, _) = await LoneStudentWithExamAsync(DateTime.UtcNow.AddMinutes(-1));
        var openStatus = await TestHost.DataAsync(await open.GetAsync("/api/exams/student/current"));
        openStatus.GetProperty("canStartNow").GetBoolean().Should().BeTrue();
        openStatus.GetProperty("secondsUntilStart").GetInt32().Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public async Task M3_I_07_EarlyJoinMinutes_OutsideZeroToSixty_Rejected(int minutes)
    {
        var teacher = await _host.AsTeacher();
        var resp = await teacher.PostAsJsonAsync("/api/exams", new
        {
            title = "Bad early join", sectionId = _host.Seed.SectionId, startTime = DateTime.UtcNow.AddHours(1),
            durationMinutes = 60, earlyJoinMinutes = minutes,
            questions = new[] { TestHost.TheoryQuestion() }, studentIds = Array.Empty<Guid>()
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("Early join");
    }

    [Fact]
    public async Task M3_I_07_EarlyJoinMinutes_RoundTripsOnExamDetail()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddHours(1),
            extra: new Dictionary<string, object?> { ["earlyJoinMinutes"] = 10 });
        var detail = await TestHost.JsonAsync(await (await _host.AsTeacher()).GetAsync($"/api/exams/{examId}"));
        detail.GetProperty("earlyJoinMinutes").GetInt32().Should().Be(10);
    }
}
