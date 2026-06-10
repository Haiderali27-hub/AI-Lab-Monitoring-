using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Analytics;

public class AnalyticsTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public AnalyticsTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    [Fact]
    public async Task GetSystemAnalytics_AsAdmin_Returns200WithAllFields()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/analytics/system");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("totalStudents").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        body.GetProperty("totalTeachers").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body.GetProperty("examsByMonth").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetSystemAnalytics_AsTeacher_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync("/api/analytics/system");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetExamSummary_AsTeacher_Returns200WithScoreDistribution()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync($"/api/analytics/exams/{_seed.ExamId}/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("scoreDistribution").ValueKind.Should().Be(JsonValueKind.Object);
        body.GetProperty("questionStats").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GetExamSummary_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync($"/api/analytics/exams/{_seed.ExamId}/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GeneratePdfReport_AsTeacher_Returns200WithDownloadUrl()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsync($"/api/analytics/exams/{_seed.ExamId}/report", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        string? url = body.GetProperty("downloadUrl").GetString();
        url.Should().Contain(".pdf");
    }

    [Fact]
    public async Task GetExamSummary_WithInvalidExamId_Returns404()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync($"/api/analytics/exams/{Guid.NewGuid()}/summary");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
