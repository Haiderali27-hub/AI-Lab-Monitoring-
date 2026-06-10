using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Student;

public class StudentPortalTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public StudentPortalTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    [Fact]
    public async Task GetStudentDashboard_AsStudent_Returns200WithRequiredFields()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalExamsTaken").ValueKind.Should().Be(JsonValueKind.Number);
        body.GetProperty("averageScore").ValueKind.Should().Be(JsonValueKind.Number);
        body.GetProperty("recentExams").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("performanceTrend").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetStudentDashboard_AsAdmin_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/student/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetMyExams_AsStudent_ReturnsOnlyAssignedExams()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/exams");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetMyViolations_AsStudent_Returns200WithViolationList()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/violations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalViolations").ValueKind.Should().Be(JsonValueKind.Number);
        body.GetProperty("violations").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetPerformanceTrend_AsStudent_ReturnsArrayForChart()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/performance-trend");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        body.Should().NotBeNull();
    }

    [Fact]
    public async Task GetExamResult_BeforeExamEnds_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync($"/api/student/exams/{_seed.ExamId}/result");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}
