using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M5 — Live Monitoring Client Service, server side (TEST_CASES.md §M5, layer I).
/// Planned contract: IMPLEMENTATION_PLAN.md §4.1 — POST /api/monitoring/screenshot (multipart),
/// POST /api/monitoring/events/batch with ClientEventId dedup, GET /api/exams/{id}/sessions/{sid}/screenshots,
/// disconnect detection BackgroundService, Storage:RetentionDays cleanup.
/// </summary>
public class Module5MonitoringAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    // Smallest well-formed JPEG-ish payload: SOI + JFIF APP0 + EOI.
    private static byte[] Jpeg(int padTo = 0)
    {
        var head = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00 };
        var body = new byte[Math.Max(0, padTo - head.Length - 2)];
        return head.Concat(body).Concat(new byte[] { 0xFF, 0xD9 }).ToArray();
    }

    private static Task<HttpResponseMessage> UploadScreenshotAsync(HttpClient c, Guid sessionId, byte[] bytes, string fileName = "screen.jpg", string contentType = "image/jpeg")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(sessionId.ToString()), "examSessionId");
        form.Add(new StringContent("Screen"), "kind");
        form.Add(new StringContent(DateTime.UtcNow.ToString("O")), "capturedAt");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return c.PostAsync("/api/monitoring/screenshot", form);
    }

    private static object Batch(Guid sessionId, IEnumerable<(Guid id, DateTime at)> events) => new
    {
        examSessionId = sessionId,
        events = events.Select((e, i) => new
        {
            clientEventId = e.id,
            eventType = i % 2 == 0 ? "FocusLost" : "Violation",
            payloadJson = $"{{\"seq\":{i}}}",
            recordedAt = e.at
        }).ToArray()
    };

    private Task<int> NonHeartbeatEventCount(Guid sid) =>
        _host.WithDbAsync(db => db.MonitoringEvents.CountAsync(e => e.ExamSessionId == sid && e.EventType != MonitoringEventType.Heartbeat));

    [Fact(Skip = "Pending: M5-I-01 — screenshot upload")]
    public async Task M5_I_01_ScreenshotUpload_Returns200_AndIsListedForStaff()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        (await UploadScreenshotAsync(student, sid, Jpeg(4096))).StatusCode.Should().Be(HttpStatusCode.OK);

        var teacher = await _host.AsTeacher();
        var list = await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/screenshots");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await TestHost.DataAsync(list)).EnumerateArray().ToList();
        rows.Should().ContainSingle();
        rows[0].GetProperty("kind").GetString().Should().Be("Screen");

        (await _host.WithDbAsync(db => Task.FromResult(Planned.Set(db, "Screenshots").Count()))).Should().Be(1);
    }

    [Fact(Skip = "Pending: M5-I-02 — screenshot size limit")]
    public async Task M5_I_02_ScreenshotOver10MB_Returns413()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        var resp = await UploadScreenshotAsync(student, sid, Jpeg(10 * 1024 * 1024 + 1));
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact(Skip = "Pending: M5-I-03 — screenshot content sniffing")]
    public async Task M5_I_03_NonImageRenamedJpg_Returns400()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        var exe = new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 }.Concat(new byte[2048]).ToArray();
        var resp = await UploadScreenshotAsync(student, sid, exe, "screen.jpg", "image/jpeg");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(Skip = "Pending: M5-I-04 — screenshot access control")]
    public async Task M5_I_04_TeacherOfOtherSection_GetScreenshots_Returns403()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await UploadScreenshotAsync(student, sid, Jpeg(1024))).StatusCode.Should().Be(HttpStatusCode.OK);

        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        var outsider = await _host.LoginAsync(email, password);
        (await outsider.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/screenshots"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Students can't list screenshots either (not even their own).
        (await student.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/screenshots"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(Skip = "Pending: M5-I-05 — batched events")]
    public async Task M5_I_05_BatchOf20Events_Returns200_And20Stored()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var events = Enumerable.Range(0, 20).Select(i => (Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-20 + i))).ToList();

        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, events))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await NonHeartbeatEventCount(sid)).Should().Be(20);
    }

    [Fact(Skip = "Pending: M5-I-06 — ClientEventId dedup")]
    public async Task M5_I_06_SameBatchTwice_IsStoredOnce()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var events = Enumerable.Range(0, 20).Select(i => (Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-20 + i))).ToList();

        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, events))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, events))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await NonHeartbeatEventCount(sid)).Should().Be(20);
    }

    [Fact(Skip = "Pending: M5-I-07 — offline replay keeps client timestamps")]
    public async Task M5_I_07_ReplayedEvents_KeepOriginalClientTimestamps()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var clientTime = DateTime.UtcNow.AddMinutes(-7);
        var id = Guid.NewGuid();

        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, new[] { (id, clientTime) })))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stored = await _host.WithDbAsync(db => db.MonitoringEvents.SingleAsync(e => e.ExamSessionId == sid && e.EventType != MonitoringEventType.Heartbeat));
        stored.RecordedAt.Should().BeCloseTo(clientTime, TimeSpan.FromSeconds(1));
    }

    [Fact(Skip = "Pending: M5-I-08 — disconnect detection BackgroundService")]
    public async Task M5_I_08_NoHeartbeat_SessionFlaggedDisconnected_AndBroadcast()
    {
        // Production default is 30 s; shortened so the test runs quickly.
        using var host = new TestHost(b =>
        {
            b.UseSetting("Monitoring:DisconnectAfterSeconds", "2");
            b.UseSetting("Monitoring:DisconnectSweepSeconds", "1");
        });
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        await TestHost.Eventually(
            () => Task.FromResult(host.Hub.Named("StudentDisconnected").Any(m => m.Target == $"exam:{host.Seed.ExamId}" && m.ArgsJson.Contains(sid.ToString()))),
            TimeSpan.FromSeconds(15), "the live monitor must be told the student went silent");

        // A fresh heartbeat brings the student back.
        (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Hub.Named("StudentReconnected").Should().NotBeEmpty();
    }

    [Fact(Skip = "Pending: M5-I-09 — screenshot retention cleanup job")]
    public async Task M5_I_09_ScreenshotOlderThanRetention_IsDeletedByJob()
    {
        using var host = new TestHost(b =>
        {
            b.UseSetting("Storage:RetentionDays", "30");
            b.UseSetting("Storage:CleanupIntervalSeconds", "1");
        });
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await UploadScreenshotAsync(student, sid, Jpeg(1024))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadScreenshotAsync(student, sid, Jpeg(1024))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Age one screenshot past the retention window.
        await host.WithDbAsync(async db =>
        {
            var first = Planned.Set(db, "Screenshots").First();
            Planned.Set(first, "CapturedAt", DateTime.UtcNow.AddDays(-31));
            await db.SaveChangesAsync();
        });

        await TestHost.Eventually(
            () => host.WithDbAsync(db => Task.FromResult(Planned.Set(db, "Screenshots").Count() == 1)),
            TimeSpan.FromSeconds(15), "the expired screenshot (row + file) must be removed and the fresh one kept");
    }
}
