using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M2 — Pre-Exam Verification &amp; Mock Test (TEST_CASES.md §M2, layer I).
/// Planned contract: IMPLEMENTATION_PLAN.md §3.2 — POST/GET /api/exams/{id}/readiness,
/// Exam.RequireReadiness / IsMock, start-session → 400 READINESS_REQUIRED.
/// </summary>
public class Module2ReadinessAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private static object Readiness(bool passed, string failing = "Camera") => new
    {
        passedAll = passed,
        machineName = "PC-01",
        results = new object[]
        {
            new { name = "Network", passed = true, detail = "120 ms", durationMs = 120 },
            new { name = "ClockSkew", passed = true, detail = "2 s", durationMs = 3 },
            new { name = "ScreenCapture", passed = true, detail = "ok", durationMs = 40 },
            new { name = failing, passed, detail = passed ? "ok" : "No device", durationMs = 500 },
            new { name = "SecureMode", passed = true, detail = "single monitor, no RDP", durationMs = 10 },
            new { name = "Disk", passed = true, detail = "12 GB free", durationMs = 1 }
        }
    };

    private Task<Guid> ReadinessExamAsync(bool requireReadiness = true, bool isMock = false) =>
        _host.CreateExamAsync(new[] { _host.Seed.StudentId, _host.Seed.Student2Id },
            extra: new Dictionary<string, object?> { ["requireReadiness"] = requireReadiness, ["isMock"] = isMock });

    private static Task<HttpResponseMessage> PostReadinessAsync(HttpClient c, Guid examId, bool passed) =>
        c.PostAsJsonAsync($"/api/exams/{examId}/readiness", Readiness(passed));

    [Fact]
    public async Task M2_I_01_SubmitReadiness_AllPass_Returns200_StoresRow_AndAuditsPassed()
    {
        var examId = await ReadinessExamAsync();
        var student = await _host.AsStudent();

        (await PostReadinessAsync(student, examId, passed: true)).StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await _host.WithDbAsync(db => Task.FromResult(Planned.Set(db, "ReadinessChecks").ToList()));
        stored.Should().ContainSingle();
        Planned.Get(stored[0], "UserId").Should().Be(_host.Seed.StudentId);
        Planned.Get(stored[0], "ExamId").Should().Be(examId);
        Planned.Get(stored[0], "PassedAll").Should().Be(true);

        (await _host.AuditLogsAsync("READINESS_PASSED")).Should().ContainSingle();
    }

    [Fact]
    public async Task M2_I_02_StartWithoutReadiness_Returns400_ReadinessRequired()
    {
        var examId = await ReadinessExamAsync(requireReadiness: true);
        var student = await _host.AsStudent();

        var resp = await _host.StartSessionRawAsync(student, examId);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("READINESS_REQUIRED");
    }

    [Fact]
    public async Task M2_I_03_StartAfterPassingCheck_Returns200()
    {
        var examId = await ReadinessExamAsync(requireReadiness: true);
        var student = await _host.AsStudent();

        (await PostReadinessAsync(student, examId, passed: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.StartSessionRawAsync(student, examId)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M2_I_04_FailedCheck_DoesNotUnlockStart()
    {
        var examId = await ReadinessExamAsync(requireReadiness: true);
        var student = await _host.AsStudent();

        (await PostReadinessAsync(student, examId, passed: false)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.AuditLogsAsync("READINESS_FAILED")).Should().ContainSingle();

        var resp = await _host.StartSessionRawAsync(student, examId);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("READINESS_REQUIRED");
    }

    [Fact]
    public async Task M2_I_05_StaleCheck_OlderThan30Min_Returns400()
    {
        var examId = await ReadinessExamAsync(requireReadiness: true);
        var student = await _host.AsStudent();
        (await PostReadinessAsync(student, examId, passed: true)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Age the stored check to T-31 min.
        await _host.WithDbAsync(async db =>
        {
            foreach (var row in Planned.Set(db, "ReadinessChecks"))
                Planned.Set(row, "CheckedAt", DateTime.UtcNow.AddMinutes(-31));
            await db.SaveChangesAsync();
        });

        var resp = await _host.StartSessionRawAsync(student, examId);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("READINESS_REQUIRED");
    }

    [Fact]
    public async Task M2_I_06_TeacherGetReadiness_ReturnsLatestPerStudent()
    {
        var examId = await ReadinessExamAsync();
        var s1 = await _host.AsStudent();
        var s2 = await _host.AsStudent2();
        (await PostReadinessAsync(s1, examId, passed: false)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostReadinessAsync(s1, examId, passed: true)).StatusCode.Should().Be(HttpStatusCode.OK);   // latest wins
        (await PostReadinessAsync(s2, examId, passed: false)).StatusCode.Should().Be(HttpStatusCode.OK);

        var teacher = await _host.AsTeacher();
        var resp = await teacher.GetAsync($"/api/exams/{examId}/readiness");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await TestHost.DataAsync(resp)).EnumerateArray().ToList();

        rows.Should().HaveCount(2, "one latest result per student");
        rows.Single(r => r.GetProperty("userId").GetGuid() == _host.Seed.StudentId).GetProperty("passedAll").GetBoolean().Should().BeTrue();
        rows.Single(r => r.GetProperty("userId").GetGuid() == _host.Seed.Student2Id).GetProperty("passedAll").GetBoolean().Should().BeFalse();

        // Students cannot read the staff view.
        (await s1.GetAsync($"/api/exams/{examId}/readiness")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task M2_I_07_MockExam_ExcludedFromAnalyticsAndPlagiarism()
    {
        var examId = await ReadinessExamAsync(requireReadiness: false, isMock: true);
        var qid = (await _host.QuestionIdsAsync(examId)).Single();
        const string copied = "A stack is LIFO and a queue is FIFO so push pop versus enqueue dequeue";

        foreach (var student in new[] { await _host.AsStudent(), await _host.AsStudent2() })
        {
            var sid = await _host.StartSessionAsync(student, examId);
            (await TestHost.SaveAnswerAsync(student, sid, qid, copied)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var teacher = await _host.AsTeacher();
        var plag = await (await teacher.GetAsync($"/api/exams/{examId}/plagiarism")).Content.ReadFromJsonAsync<List<JsonElement>>();
        plag.Should().BeEmpty("identical answers on a practice exam are not plagiarism cases");

        var dashboard = await (await (await _host.AsStudent()).GetAsync("/api/student/dashboard")).Content.ReadFromJsonAsync<JsonElement>();
        dashboard.GetProperty("totalExamsTaken").GetInt32().Should().Be(0, "practice attempts are not counted as exams taken");
    }

    [Fact]
    public async Task M2_I_08_MockExam_StartTwiceAfterSubmit_CreatesNewSessionEachTime()
    {
        var examId = await ReadinessExamAsync(requireReadiness: false, isMock: true);
        var student = await _host.AsStudent();

        var first = await _host.StartSessionAsync(student, examId);
        (await TestHost.SubmitAsync(student, first)).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await _host.StartSessionAsync(student, examId);
        second.Should().NotBe(first);
        (await TestHost.SubmitAsync(student, second)).StatusCode.Should().Be(HttpStatusCode.OK);

        var third = await _host.StartSessionAsync(student, examId);
        third.Should().NotBe(first).And.NotBe(second);
    }

    [Fact]
    public async Task M2_I_09_DesktopStartPath_GatedOnReadiness_AndStatusReportsIt()
    {
        var examId = await ReadinessExamAsync(requireReadiness: true);
        var student = await _host.AsStudent();
        // Seeded exams compete for "current"; end them so the dashboard shows this one.
        await _host.WithDbAsync(async db =>
        {
            foreach (var e in db.Exams.Where(e => e.ExamId != examId)) e.Status = Backend_API.Models.Enums.ExamStatus.Ended;
            await db.SaveChangesAsync();
        });

        var current = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));
        current.GetProperty("examId").GetGuid().Should().Be(examId);
        current.GetProperty("requireReadiness").GetBoolean().Should().BeTrue();
        current.GetProperty("readinessPassed").GetBoolean().Should().BeFalse();

        var blocked = await student.PostAsync("/api/exams/student/start", null);
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(blocked)).Should().Be("READINESS_REQUIRED");

        (await PostReadinessAsync(student, examId, passed: true)).StatusCode.Should().Be(HttpStatusCode.OK);
        current = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));
        current.GetProperty("readinessPassed").GetBoolean().Should().BeTrue();
        (await student.PostAsync("/api/exams/student/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M2_I_10_RequiredWebcam_NotReported_IsNotAPass()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId },
            extra: new Dictionary<string, object?> { ["requireReadiness"] = true, ["requireWebcam"] = true });
        var student = await _host.AsStudent();

        // Client claims "all passed" but never ran the camera check.
        var resp = await student.PostAsJsonAsync($"/api/exams/{examId}/readiness", new
        {
            passedAll = true,
            machineName = "PC-01",
            results = new[] { new { name = "Network", passed = true, detail = "ok", durationMs = 5 } }
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHost.JsonAsync(resp);
        body.GetProperty("passedAll").GetBoolean().Should().BeFalse();
        body.GetProperty("missingRequired").EnumerateArray().Select(e => e.GetString()).Should().Contain("Camera");

        (await TestHost.ErrorCodeAsync(await _host.StartSessionRawAsync(student, examId))).Should().Be("READINESS_REQUIRED");
    }

    [Fact]
    public async Task M2_I_11_ReadinessForUnassignedExam_Returns404()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.Student2Id });
        var student = await _host.AsStudent();
        (await PostReadinessAsync(student, examId, passed: true)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task M2_I_12_PracticeExam_ListedSeparately_NotPickedAsCurrentExam()
    {
        var mockId = await ReadinessExamAsync(requireReadiness: false, isMock: true);
        var student = await _host.AsStudent();

        var practice = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/practice"));
        practice.EnumerateArray().Select(p => p.GetProperty("examId").GetGuid()).Should().Contain(mockId);

        var current = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));
        if (current.TryGetProperty("examId", out var cur) && cur.ValueKind != JsonValueKind.Null)
            cur.GetGuid().Should().NotBe(mockId, "the dashboard's real exam is never a practice exam");

        var sid = await _host.StartSessionAsync(student, mockId);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        practice = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/practice"));
        practice.EnumerateArray().Single(p => p.GetProperty("examId").GetGuid() == mockId)
            .GetProperty("attempts").GetInt32().Should().Be(1);
    }
}
