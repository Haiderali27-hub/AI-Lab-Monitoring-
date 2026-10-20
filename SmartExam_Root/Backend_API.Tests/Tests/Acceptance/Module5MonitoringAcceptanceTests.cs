using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
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

    [Fact]
    public async Task M5_I_02_ScreenshotOver10MB_Returns413()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        var resp = await UploadScreenshotAsync(student, sid, Jpeg(10 * 1024 * 1024 + 1));
        resp.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task M5_I_03_NonImageRenamedJpg_Returns400()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        var exe = new byte[] { (byte)'M', (byte)'Z', 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 }.Concat(new byte[2048]).ToArray();
        var resp = await UploadScreenshotAsync(student, sid, exe, "screen.jpg", "image/jpeg");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
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

    [Fact]
    public async Task M5_I_05_BatchOf20Events_Returns200_And20Stored()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var events = Enumerable.Range(0, 20).Select(i => (Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-20 + i))).ToList();

        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, events))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await NonHeartbeatEventCount(sid)).Should().Be(20);
    }

    [Fact]
    public async Task M5_I_06_SameBatchTwice_IsStoredOnce()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var events = Enumerable.Range(0, 20).Select(i => (Guid.NewGuid(), DateTime.UtcNow.AddSeconds(-20 + i))).ToList();

        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, events))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, events))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await NonHeartbeatEventCount(sid)).Should().Be(20);
    }

    [Fact]
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

    [Fact]
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

    [Fact]
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

    // ── Stage S5 additions: evidence content, upload rules, batching edge cases, timeline, liveness ──────────

    private static MultipartFormDataContent Form(Guid sessionId, byte[] bytes, string kind = "Screen", string? trigger = null, string? reason = null, string? capturedAt = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(sessionId.ToString()), "examSessionId");
        form.Add(new StringContent(kind), "kind");
        form.Add(new StringContent(capturedAt ?? DateTime.UtcNow.ToString("O")), "capturedAt");
        if (trigger is not null) form.Add(new StringContent(trigger), "trigger");
        if (reason is not null) form.Add(new StringContent(reason), "reason");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "capture.jpg");
        return form;
    }

    private async Task<(Guid Id, string Email, string Password)> InvigilatorAsync() => await _host.CreateUserAsync("Invigilator");

    [Fact]
    public async Task M5_I_10_StaffFetchesTheStoredImage_ByteForByte_AndProctorsAreTold()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var bytes = Jpeg(2048);

        var upload = await student.PostAsync("/api/monitoring/screenshots",   // the alias the web simulator uses
            Form(sid, bytes, trigger: "Violation", reason: "UnauthorizedProcess"));
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var shotId = (await TestHost.DataAsync(upload)).GetProperty("screenshotId").GetGuid();

        _host.Hub.Named("ScreenshotUploaded").Should().ContainSingle(m =>
            m.Target == $"exam:{_host.Seed.ExamId}" && m.ArgsJson.Contains(shotId.ToString()) && m.ArgsJson.Contains("UnauthorizedProcess"));

        var teacher = await _host.AsTeacher();
        var rows = (await TestHost.DataAsync(await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/screenshots"))).EnumerateArray().ToList();
        rows.Should().ContainSingle();
        rows[0].GetProperty("trigger").GetString().Should().Be("Violation");
        rows[0].GetProperty("triggerReason").GetString().Should().Be("UnauthorizedProcess");
        rows[0].GetProperty("sizeBytes").GetInt32().Should().Be(bytes.Length);
        var sha = rows[0].GetProperty("sha256").GetString();
        sha.Should().Be(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());

        var content = await teacher.GetAsync(rows[0].GetProperty("contentUrl").GetString());
        content.StatusCode.Should().Be(HttpStatusCode.OK);
        content.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        content.Headers.CacheControl!.Private.Should().BeTrue();
        (await content.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        // The file lives under {EvidencePath}/{examId}/{sessionId}/ with a server-generated name.
        var stored = await _host.WithDbAsync(db => db.Screenshots.SingleAsync());
        stored.StoragePath.Should().StartWith($"{_host.Seed.ExamId}/{sid}/").And.NotContain("capture.jpg");

        // Students never read captures, not even their own; unknown ids are 404.
        (await student.GetAsync($"/api/screenshots/{shotId}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await teacher.GetAsync($"/api/screenshots/{Guid.NewGuid()}/content")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task M5_I_11_Upload_RefusesForeignSessions_FinishedSessions_AndUnwantedWebcamImages()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var other = await _host.AsStudent2();

        // Another student's session: not found (no telemetry into someone else's session).
        (await other.PostAsync("/api/monitoring/screenshot", Form(sid, Jpeg(512)))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The seeded exam does not require a webcam: a webcam image is refused (privacy), a PNG screen capture is fine.
        var webcam = await student.PostAsync("/api/monitoring/screenshot", Form(sid, Jpeg(512), kind: "Webcam"));
        webcam.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(webcam)).Should().Be("WEBCAM_NOT_REQUIRED");
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(new byte[256]).ToArray();
        (await student.PostAsync("/api/monitoring/screenshot", Form(sid, png))).StatusCode.Should().Be(HttpStatusCode.OK);

        // An unknown kind is a bad request; an absurd capture time is replaced by the server's.
        (await student.PostAsync("/api/monitoring/screenshot", Form(sid, Jpeg(512), kind: "Desktop"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await student.PostAsync("/api/monitoring/screenshot", Form(sid, Jpeg(512), capturedAt: "2001-01-01T00:00:00Z"))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.WithDbAsync(db => db.Screenshots.OrderBy(s => s.CapturedAt).FirstAsync())).CapturedAt
            .Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        // After submit nothing more is captured.
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        var late = await student.PostAsync("/api/monitoring/screenshot", Form(sid, Jpeg(512)));
        late.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TestHost.ErrorCodeAsync(late)).Should().Be("SESSION_NOT_ACTIVE");
    }

    [Fact]
    public async Task M5_I_12_AssignedInvigilator_SeesCaptures_UnassignedOneDoesNot()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await UploadScreenshotAsync(student, sid, Jpeg(512))).StatusCode.Should().Be(HttpStatusCode.OK);
        var shotId = await _host.WithDbAsync(db => db.Screenshots.Select(s => s.ScreenshotId).SingleAsync());

        var (assignedId, assignedEmail, assignedPassword) = await InvigilatorAsync();
        var (_, otherEmail, otherPassword) = await InvigilatorAsync();
        var teacher = await _host.AsTeacher();
        (await teacher.PostAsJsonAsync($"/api/exams/{_host.Seed.ExamId}/proctors", new { userId = assignedId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var assigned = await _host.LoginAsync(assignedEmail, assignedPassword);
        (await assigned.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/screenshots")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await assigned.GetAsync($"/api/screenshots/{shotId}/content")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await assigned.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline")).StatusCode.Should().Be(HttpStatusCode.OK);

        var unassigned = await _host.LoginAsync(otherEmail, otherPassword);
        (await unassigned.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/screenshots")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await unassigned.GetAsync($"/api/screenshots/{shotId}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await unassigned.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // A session of another exam cannot be read through this exam's URL.
        var admin = await _host.AsAdmin();
        (await admin.GetAsync($"/api/exams/{Guid.NewGuid()}/sessions/{sid}/screenshots")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var otherExam = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });
        (await admin.GetAsync($"/api/exams/{otherExam}/sessions/{sid}/screenshots")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task M5_I_13_Batch_ValidatesAllOrNothing_ClampsAbsurdTimes_AndOnlyAlertsForFreshViolations()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        // Over the limit, foreign session, or one bad payload: rejected, nothing stored.
        var tooMany = Enumerable.Range(0, 201).Select(_ => (Guid.NewGuid(), DateTime.UtcNow)).ToList();
        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, tooMany))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var other = await _host.AsStudent2();
        (await other.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, new[] { (Guid.NewGuid(), DateTime.UtcNow) })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var bad = new
        {
            examSessionId = sid,
            events = new object[]
            {
                new { clientEventId = Guid.NewGuid(), eventType = "FocusLost", payloadJson = "{}", recordedAt = DateTime.UtcNow },
                new { clientEventId = Guid.NewGuid(), eventType = "FocusLost", payloadJson = "{not json", recordedAt = DateTime.UtcNow }
            }
        };
        (await student.PostAsJsonAsync("/api/monitoring/events/batch", bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await NonHeartbeatEventCount(sid)).Should().Be(0);

        var future = Guid.NewGuid();
        var old = Guid.NewGuid();
        var forged = Guid.NewGuid();
        var batch = new
        {
            examSessionId = sid,
            events = new object[]
            {
                // A client clock far ahead: stored at server time, flagged.
                new { clientEventId = future, eventType = "Violation", payloadJson = "{\"reason\":\"KeyBlocked\"}", recordedAt = DateTime.UtcNow.AddHours(3) },
                // Replayed after a 10-minute outage: kept at its own time, no live alert.
                new { clientEventId = old, eventType = "Violation", payloadJson = "{\"reason\":\"ProcessKilled\"}", recordedAt = DateTime.UtcNow.AddMinutes(-10) },
                // A client cannot forge a server finding.
                new { clientEventId = forged, eventType = "Disconnected", payloadJson = "{}", recordedAt = DateTime.UtcNow }
            }
        };
        var resp = await student.PostAsJsonAsync("/api/monitoring/events/batch", batch);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await TestHost.DataAsync(resp);
        data.GetProperty("accepted").GetInt32().Should().Be(3);
        data.GetProperty("duplicates").GetInt32().Should().Be(0);

        var stored = await _host.WithDbAsync(db => db.MonitoringEvents.Where(e => e.ExamSessionId == sid).ToListAsync());
        var f = stored.Single(e => e.ClientEventId == future);
        f.RecordedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        f.Payload.Should().Contain("clockAdjusted");
        f.EventType.Should().Be(MonitoringEventType.KeyBlocked);
        var o = stored.Single(e => e.ClientEventId == old);
        o.RecordedAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(-10), TimeSpan.FromSeconds(5));
        o.ReceivedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        stored.Single(e => e.ClientEventId == forged).EventType.Should().Be(MonitoringEventType.WindowFocus);

        var alerts = _host.Hub.Named("ViolationEvent").ToList();
        alerts.Should().ContainSingle("only the fresh violation is pushed live");
        alerts[0].ArgsJson.Should().Contain("KeyBlocked");
    }

    [Fact]
    public async Task M5_I_14_SessionTimeline_MergesEventsCapturesWarningsAndStatus_NewestFirst()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.PostEventAsync(student, sid, "Violation", "{\"reason\":\"FocusLost\",\"activeWindow\":\"chrome\"}"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadScreenshotAsync(student, sid, Jpeg(512))).StatusCode.Should().Be(HttpStatusCode.OK);
        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.TeacherId, "Teacher")))
            await hub.Hub.SendWarning(_host.Seed.StudentId.ToString(), "Eyes on your own screen");
        var checkpoint = DateTime.UtcNow;
        await Task.Delay(20);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var teacher = await _host.AsTeacher();
        var resp = await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await TestHost.DataAsync(resp)).EnumerateArray().ToList();

        items.Select(i => i.GetProperty("kind").GetString()).Should().Contain(new[] { "Event", "Screenshot", "Warning", "Status" });
        items.Should().NotContain(i => i.GetProperty("type").GetString() == "Heartbeat");
        items.Should().Contain(i => i.GetProperty("type").GetString() == "FocusLost" && i.GetProperty("severity").GetString() == "Warning"
                                    && i.GetProperty("detail").GetString()!.Contains("chrome"));
        items.Should().Contain(i => i.GetProperty("kind").GetString() == "Screenshot" && i.GetProperty("screenshotId").GetGuid() != Guid.Empty);
        items.Should().Contain(i => i.GetProperty("kind").GetString() == "Warning" && i.GetProperty("detail").GetString() == "Eyes on your own screen");
        items.First().GetProperty("type").GetString().Should().Be("Submitted", "newest first: the submit is the last thing that happened");
        items.Last().GetProperty("type").GetString().Should().Be("Started");
        var times = items.Select(i => i.GetProperty("at").GetDateTime()).ToList();
        times.Should().BeInDescendingOrder();

        // Incremental refresh: only what happened after the checkpoint.
        var newer = (await TestHost.DataAsync(await teacher.GetAsync(
            $"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline?after={Uri.EscapeDataString(checkpoint.ToString("O"))}"))).EnumerateArray().ToList();
        newer.Select(i => i.GetProperty("type").GetString()).Should().Equal("Submitted");

        // Outsiders and students are refused.
        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        (await (await _host.LoginAsync(email, password)).GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.GetAsync($"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task M5_I_15_Heartbeat_StampsLiveness_StoresOnlyChanges_AndCarriesIdleAndCaptureSettings()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);

        object Beat(string processes, int idle) => new
        {
            examSessionId = sid, isForegroundExamApp = true, activeWindowTitle = "SmartExam", processListSnapshot = processes,
            idleSeconds = idle, cameraStatus = "Off"
        };
        var first = await student.PostAsJsonAsync("/api/monitoring/heartbeat", Beat("SmartExam", 3));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var ack = await TestHost.DataAsync(first);
        ack.GetProperty("screenshotIntervalSeconds").GetInt32().Should().Be(60);
        ack.GetProperty("webcamIntervalSeconds").GetInt32().Should().Be(60);
        ack.GetProperty("requireWebcam").GetBoolean().Should().BeFalse();

        // Same report (only the idle counter moved): liveness is updated, no second Heartbeat row.
        (await student.PostAsJsonAsync("/api/monitoring/heartbeat", Beat("SmartExam", 13))).StatusCode.Should().Be(HttpStatusCode.OK);
        int Beats(List<Backend_API.Models.MonitoringEvent> e) => e.Count(x => x.EventType == MonitoringEventType.Heartbeat);
        var events = await _host.WithDbAsync(db => db.MonitoringEvents.Where(e => e.ExamSessionId == sid).ToListAsync());
        Beats(events).Should().Be(1);
        // A new app appeared: stored.
        (await student.PostAsJsonAsync("/api/monitoring/heartbeat", Beat("SmartExam,chrome", 0))).StatusCode.Should().Be(HttpStatusCode.OK);
        events = await _host.WithDbAsync(db => db.MonitoringEvents.Where(e => e.ExamSessionId == sid).ToListAsync());
        Beats(events).Should().Be(2);

        var session = await _host.WithDbAsync(db => db.ExamSessions.SingleAsync(s => s.SessionId == sid));
        session.LastHeartbeatAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        session.IsDisconnected.Should().BeFalse();

        var live = _host.Hub.Named("StudentHeartbeat").Last();
        live.ArgsJson.Should().Contain("\"processes\":\"SmartExam,chrome\"").And.Contain("\"idleSeconds\":0")
            .And.Contain("\"cameraStatus\":\"Off\"").And.Contain("\"isDisconnected\":false");
    }

    [Fact]
    public async Task M5_I_16_Watchdog_FlagsSilentRunningSessionsOnce_AndIgnoresFinishedOnes()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        var other = await _host.AsStudent2();
        var finished = await _host.StartSessionAsync(other, _host.Seed.ExamId);
        (await TestHost.SubmitAsync(other, finished)).StatusCode.Should().Be(HttpStatusCode.OK);

        var watchdog = _host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<Backend_API.Services.Monitoring.HeartbeatWatchdogService>().Single();
        var later = DateTime.UtcNow.AddMinutes(1);
        (await watchdog.SweepAsync(later, TimeSpan.FromSeconds(30))).Should().Be(1);
        (await watchdog.SweepAsync(later.AddSeconds(5), TimeSpan.FromSeconds(30))).Should().Be(0, "already flagged");

        var session = await _host.WithDbAsync(db => db.ExamSessions.SingleAsync(s => s.SessionId == sid));
        session.IsDisconnected.Should().BeTrue();
        (await _host.WithDbAsync(db => db.MonitoringEvents.CountAsync(e => e.ExamSessionId == sid && e.EventType == MonitoringEventType.Disconnected)))
            .Should().Be(1);
        (await _host.WithDbAsync(db => db.ExamSessions.SingleAsync(s => s.SessionId == finished))).IsDisconnected.Should().BeFalse();
        _host.Hub.Named("StudentDisconnected").Should().ContainSingle(m => m.ArgsJson.Contains(sid.ToString()));

        // The next heartbeat clears the flag, records how long the PC was silent, and says so.
        (await TestHost.HeartbeatAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.WithDbAsync(db => db.ExamSessions.SingleAsync(s => s.SessionId == sid))).IsDisconnected.Should().BeFalse();
        (await _host.WithDbAsync(db => db.MonitoringEvents.SingleAsync(e => e.ExamSessionId == sid && e.EventType == MonitoringEventType.Reconnected)))
            .Payload.Should().Contain("offlineSeconds");
        _host.Hub.Named("StudentReconnected").Should().ContainSingle(m => m.Target == $"exam:{_host.Seed.ExamId}");
        // Liveness events are not violations: the tile stays Normal.
        _host.Hub.Named("StudentHeartbeat").Last().ArgsJson.Should().Contain("\"tileStatus\":\"Normal\"");
    }

    [Fact]
    public async Task M5_I_17_Retention_RemovesRowsWhoseFileIsAlreadyGone_AndOrphanedFiles()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await UploadScreenshotAsync(student, sid, Jpeg(512))).StatusCode.Should().Be(HttpStatusCode.OK);

        var storage = _host.Services.GetRequiredService<Backend_API.Services.Monitoring.EvidenceStorage>();
        var shot = await _host.WithDbAsync(db => db.Screenshots.SingleAsync());
        var file = storage.Resolve(shot.StoragePath)!;
        File.Exists(file).Should().BeTrue();
        File.Delete(file);   // e.g. removed by hand

        // An orphan: a file no row points at, older than the window.
        var orphan = Path.Combine(storage.Root, Guid.NewGuid().ToString(), "stray.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(orphan)!);
        await File.WriteAllBytesAsync(orphan, Jpeg(64));
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-40));

        var job = _host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>()
            .OfType<Backend_API.Services.Monitoring.EvidenceRetentionService>().Single();
        (await job.SweepAsync(DateTime.UtcNow.AddMinutes(1))).Should().Be(1);
        (await _host.WithDbAsync(db => db.Screenshots.CountAsync())).Should().Be(0);
        File.Exists(orphan).Should().BeFalse();
    }

    [Fact]
    public async Task M5_I_18_ScreenshotUploads_AreRateLimitedPerSession()
    {
        using var host = new TestHost(b => b.UseSetting("Monitoring:MaxScreenshotsPerMinute", "2"));
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await UploadScreenshotAsync(student, sid, Jpeg(256))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadScreenshotAsync(student, sid, Jpeg(256))).StatusCode.Should().Be(HttpStatusCode.OK);
        var third = await UploadScreenshotAsync(student, sid, Jpeg(256));
        third.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await TestHost.ErrorCodeAsync(third)).Should().Be("TOO_MANY_CAPTURES");
    }

    // ── S5 verifier regressions ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task M5_I_19_TimelineAfter_StillDeliversEventsReplayedWithAnOldClientTime()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var teacher = await _host.AsTeacher();
        var checkpoint = DateTime.UtcNow;   // the drawer's last refresh
        await Task.Delay(20);

        // The PC was offline: a violation from 5 minutes ago arrives only now, with its original time.
        var clientTime = DateTime.UtcNow.AddMinutes(-5);
        (await student.PostAsJsonAsync("/api/monitoring/events/batch", Batch(sid, new[] { (Guid.NewGuid(), clientTime) })))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var newer = (await TestHost.DataAsync(await teacher.GetAsync(
            $"/api/exams/{_host.Seed.ExamId}/sessions/{sid}/timeline?after={Uri.EscapeDataString(checkpoint.ToString("O"))}"))).EnumerateArray().ToList();
        var replayed = newer.Should().ContainSingle(i => i.GetProperty("kind").GetString() == "Event",
            "an incremental refresh must not lose an event just because its client time is older than the cursor").Subject;
        replayed.GetProperty("at").GetDateTime().ToUniversalTime().Should().BeCloseTo(clientTime, TimeSpan.FromSeconds(1));
        replayed.GetProperty("receivedAt").GetDateTime().ToUniversalTime().Should().BeAfter(checkpoint);
    }

    // Makes the next heartbeat's SaveChanges race a finalisation: just before it writes, another context
    // force-submits the session (ExamSession.Status is a concurrency token), and the save fails with the
    // DbUpdateConcurrencyException Postgres would raise. It is thrown here, before anything is written, because the
    // InMemory provider is not transactional (it would keep the rows inserted before the failing update, which
    // Postgres rolls back), so the heartbeat's retry path runs exactly as it does in production.
    private sealed class FinaliseDuringHeartbeat : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        private int _armed;
        private Guid _sessionId;
        public int ConcurrencyFailures;

        public void Arm(Guid sessionId)
        {
            _sessionId = sessionId;
            Interlocked.Exchange(ref _armed, 1);
        }

        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var context = eventData.Context;
            if (context is not null &&
                context.ChangeTracker.Entries<Backend_API.Models.ExamSession>().Any(e =>
                    e.Entity.SessionId == _sessionId && e.State == EntityState.Modified && e.Property(s => s.LastHeartbeatAt).IsModified) &&
                Interlocked.Exchange(ref _armed, 0) == 1)
            {
                var options = (DbContextOptions<Backend_API.Data.AppDbContext>)context.GetService<IDbContextOptions>();
                await using var other = new Backend_API.Data.AppDbContext(options);
                var session = await other.ExamSessions.SingleAsync(s => s.SessionId == _sessionId, cancellationToken);
                session.Status = SessionStatus.ForceSubmitted;
                session.SubmittedAt = DateTime.UtcNow;
                await other.SaveChangesAsync(cancellationToken);
                Interlocked.Increment(ref ConcurrencyFailures);
                throw new DbUpdateConcurrencyException("The session was finalised concurrently (simulated).");
            }
            return result;
        }
    }

    [Fact]
    public async Task M5_I_20_ReconnectingHeartbeat_RacingAFinalisation_StillClearsTheDisconnectedFlag()
    {
        var race = new FinaliseDuringHeartbeat();
        using var raced = new TestHost(b => b.ConfigureTestServices(s =>
            s.ConfigureDbContext<Backend_API.Data.AppDbContext>(o => o.AddInterceptors(race))));
        var rs = await raced.AsStudent();
        var rsid = await raced.StartSessionAsync(rs, raced.Seed.ExamId);
        await raced.WithDbAsync(async db =>
        {
            var s = await db.ExamSessions.SingleAsync(x => x.SessionId == rsid);
            s.IsDisconnected = true;
            s.DisconnectedAt = DateTime.UtcNow.AddSeconds(-40);
            s.LastHeartbeatAt = DateTime.UtcNow.AddSeconds(-70);
            await db.SaveChangesAsync();
        });
        race.Arm(rsid);

        (await TestHost.HeartbeatAsync(rs, rsid)).StatusCode.Should().Be(HttpStatusCode.OK);

        race.ConcurrencyFailures.Should().Be(1, "the heartbeat's first save must have collided with the force-submit");
        var after = await raced.WithDbAsync(db => db.ExamSessions.SingleAsync(x => x.SessionId == rsid));
        after.Status.Should().Be(SessionStatus.ForceSubmitted, "the finalisation wins");
        after.IsDisconnected.Should().BeFalse("the reconnect is re-applied after the reload");
        after.DisconnectedAt.Should().BeNull();
        after.LastHeartbeatAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(30));
        (await raced.WithDbAsync(db => db.MonitoringEvents.CountAsync(e => e.ExamSessionId == rsid && e.EventType == MonitoringEventType.Reconnected)))
            .Should().Be(1);
    }
}
