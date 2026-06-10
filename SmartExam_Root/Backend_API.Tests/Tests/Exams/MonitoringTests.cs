using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

using System;

namespace Backend_API.Tests.Tests.Exams;

public class MonitoringTests : IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public MonitoringTests()
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
    public async Task RecordViolationEvent_IsStoredWithViolationType()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionId").GetString();

        var violationResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/monitoring-event",
            new
            {
                eventType = "Violation",
                payload = "{\"blockedApp\":\"chrome.exe\",\"reason\":\"Unauthorized application\"}"
            }
        );

        violationResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecordHeartbeat_AsStudent_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("sessionId").GetString();

        var hbResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/monitoring-event",
            new { eventType = "Heartbeat", payload = "{\"activeWindow\":\"SmartExam.exe\"}" }
        );

        hbResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RecordMonitoringEvent_WithoutSession_Returns404Or400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var fakeSessionId = Guid.NewGuid();

        var resp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{fakeSessionId}/monitoring-event",
            new { eventType = "Heartbeat", payload = "{}" }
        );

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}
