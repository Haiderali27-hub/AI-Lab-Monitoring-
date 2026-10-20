using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Tests.Helpers;
using Backend_API.Tests.Tests.Acceptance;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Exams;

/// <summary>
/// Module 8 monitoring settings (M8-E-02 backend half): per-exam mean screenshot / webcam
/// intervals, validated 10–600 s, persisted on create + edit and handed to the desktop client.
/// </summary>
public class Module8CaptureSettingsTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private async Task<JsonElement> ExamAsync(Guid examId)
    {
        var teacher = await _host.AsTeacher();
        return await (await teacher.GetAsync($"/api/exams/{examId}")).Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Defaults_Are60Seconds()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });
        var exam = await ExamAsync(examId);
        exam.GetProperty("screenshotIntervalSeconds").GetInt32().Should().Be(60);
        exam.GetProperty("webcamIntervalSeconds").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task Intervals_PersistOnCreate_AndReachTheStudentClient()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.Student2Id }, extra: new Dictionary<string, object?>
        {
            ["screenshotIntervalSeconds"] = 30,
            ["webcamIntervalSeconds"] = 120
        });

        var exam = await ExamAsync(examId);
        exam.GetProperty("screenshotIntervalSeconds").GetInt32().Should().Be(30);
        exam.GetProperty("webcamIntervalSeconds").GetInt32().Should().Be(120);

        // Student2's earliest live assignment is the seeded exam; move it out of the way.
        await _host.WithDbAsync(async db =>
        {
            db.ExamAssignments.RemoveRange(db.ExamAssignments.Where(a => a.ExamId == _host.Seed.ExamId && a.UserId == _host.Seed.Student2Id));
            await db.SaveChangesAsync();
        });
        var student = await _host.AsStudent2();
        var current = await TestHost.DataAsync(await student.GetAsync("/api/exams/student/current"));
        current.GetProperty("examId").GetGuid().Should().Be(examId);
        current.GetProperty("screenshotIntervalSeconds").GetInt32().Should().Be(30);
        current.GetProperty("webcamIntervalSeconds").GetInt32().Should().Be(120);
    }

    [Theory]
    [InlineData(9, 60)]
    [InlineData(601, 60)]
    [InlineData(60, 0)]
    [InlineData(60, 1000)]
    public async Task OutOfRangeIntervals_Return400(int screenshot, int webcam)
    {
        var teacher = await _host.AsTeacher();
        var resp = await teacher.PostAsJsonAsync("/api/exams", new
        {
            title = "Bad intervals", sectionId = _host.Seed.SectionId, startTime = DateTime.UtcNow.AddHours(1), durationMinutes = 30,
            questions = new[] { TestHost.TheoryQuestion() }, studentIds = Array.Empty<Guid>(),
            screenshotIntervalSeconds = screenshot, webcamIntervalSeconds = webcam
        });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("interval");
    }

    [Fact]
    public async Task Intervals_PersistOnEdit()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, startUtc: DateTime.UtcNow.AddHours(2));
        var teacher = await _host.AsTeacher();
        var resp = await teacher.PutAsJsonAsync($"/api/exams/{examId}", new
        {
            title = "Edited", sectionId = _host.Seed.SectionId, startTime = DateTime.UtcNow.AddHours(2), durationMinutes = 30,
            questions = new[] { TestHost.TheoryQuestion() }, studentIds = new[] { _host.Seed.StudentId },
            screenshotIntervalSeconds = 45, webcamIntervalSeconds = 90
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());

        var exam = await ExamAsync(examId);
        exam.GetProperty("screenshotIntervalSeconds").GetInt32().Should().Be(45);
        exam.GetProperty("webcamIntervalSeconds").GetInt32().Should().Be(90);
    }
}
