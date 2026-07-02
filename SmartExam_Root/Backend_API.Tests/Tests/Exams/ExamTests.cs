using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

using System;

namespace Backend_API.Tests.Tests.Exams;

public class ExamTests : IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public ExamTests()
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

    [Fact]
    public async Task GetExams_AsTeacher_Returns200WithExamList()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync("/api/exams");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetExams_AsStudent_ReturnsOnlyAssignedExams()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/exams");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
        foreach (var exam in body!)
        {
            // student view should map properly
            exam.TryGetProperty("isEligible", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task GetExamById_AsTeacher_Returns200WithQuestions()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync($"/api/exams/{_seed.ExamId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var questions = body.GetProperty("questions");
        questions.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task StartExamSession_AsEligibleStudent_Returns200WithSessionId()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        string? sessionId = body.GetProperty("sessionId").GetString();
        sessionId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveAnswer_DuringActiveSession_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionBody.GetProperty("sessionId").GetString();

        var saveResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/save-answer",
            new { questionId = _seed.Question1Id, answerText = "string reverse(string s) { return s; }" }
        );

        saveResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecordMonitoringEvent_AsStudent_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionBody.GetProperty("sessionId").GetString();

        var eventResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/monitoring-event",
            new
            {
                eventType = "Heartbeat",
                payload = "{\"activeWindow\":\"code.exe\",\"timestamp\":\"2025-01-01T10:00:00Z\"}"
            }
        );

        eventResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SubmitExam_AsStudent_Returns200WithSubmittedAt()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionBody.GetProperty("sessionId").GetString();

        var submitResp = await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitBody = await submitResp.Content.ReadFromJsonAsync<JsonElement>();
        submitBody.GetProperty("submittedAt").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubmitExam_Twice_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = sessionBody.GetProperty("sessionId").GetString();

        await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);
        var secondSubmit = await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);

        secondSubmit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
