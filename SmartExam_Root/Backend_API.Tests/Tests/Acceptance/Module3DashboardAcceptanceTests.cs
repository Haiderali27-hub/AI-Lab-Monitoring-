using System.Net;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M3 — Student Exam Dashboard (TEST_CASES.md §M3, layer I).
/// Planned contract: IMPLEMENTATION_PLAN.md §3.4 — start-session before StartTime → 400 EXAM_NOT_STARTED,
/// honouring Exam.EarlyJoinMinutes.
/// </summary>
public class Module3DashboardAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact(Skip = "Pending: M3-I-01 — server rejects start before StartTime")]
    public async Task M3_I_01_StartBeforeStartTime_Returns400_ExamNotStarted()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddHours(1));
        var student = await _host.AsStudent();

        var resp = await _host.StartSessionRawAsync(student, examId);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("EXAM_NOT_STARTED");
        (await _host.WithDbAsync(db => db.ExamSessions.CountAsync(s => s.ExamId == examId))).Should().Be(0);
    }

    [Fact(Skip = "Pending: M3-I-02 — Exam.EarlyJoinMinutes window")]
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
}
