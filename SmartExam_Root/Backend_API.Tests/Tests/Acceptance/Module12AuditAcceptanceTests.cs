using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend_API.Models;
using Backend_API.Services;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M12 — Logs, Analytics &amp; Audit Trail (TEST_CASES.md §M12, layers U (via API) and I).
/// M12-U-04 (canonical JSON key order) is a pure unit test of the hashing helper — not expressed here.
/// Planned contract: IMPLEMENTATION_PLAN.md §5.2 — AuditLog.Sequence/PrevHash/Hash, GET /api/audit-logs/verify
/// → { valid, brokenAt }, POST /api/analytics/reports/{attendance|violations|scores|evidence-zip|excel}
/// with body { examId } → { downloadUrl }. Backfill trigger assumed at POST /api/audit-logs/backfill.
/// </summary>
public class Module12AuditAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private async Task GenerateSomeAuditTrafficAsync()
    {
        await _host.AsAdmin();
        await _host.AsTeacher();
        await _host.NewClient().PostAsJsonAsync("/api/auth/login", new { email = SeededData.AdminEmail, password = "wrong" });
    }

    private async Task<JsonElement> VerifyAsync()
    {
        var resp = await (await _host.AsAdmin()).GetAsync("/api/audit-logs/verify");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return await TestHost.DataAsync(resp);
    }

    private async Task<List<AuditLog>> ChainAsync() =>
        (await _host.WithDbAsync(db => db.AuditLogs.AsNoTracking().ToListAsync()))
        .OrderBy(l => Convert.ToInt64(Planned.Get(l, "Sequence"))).ToList();

    private async Task<byte[]> GenerateReportAsync(string type, string expectedContentType)
    {
        var teacher = await _host.AsTeacher();
        var gen = await teacher.PostAsJsonAsync($"/api/analytics/reports/{type}", new { examId = _host.Seed.ExamId });
        gen.StatusCode.Should().Be(HttpStatusCode.OK);
        var url = (await TestHost.DataAsync(gen)).GetProperty("downloadUrl").GetString()!;

        var file = await teacher.GetAsync(url);
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.Should().Be(expectedContentType);
        return await file.Content.ReadAsByteArrayAsync();
    }

    private async Task<Guid> SubmittedSessionWithViolationAsync()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.PostEventAsync(student, sid, "Violation", "{\"blockedApp\":\"chrome.exe\"}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "LIFO / FIFO")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        return sid;
    }

    // ── U rows expressed through the API ─────────────────────────────────────

    [Fact]
    public async Task M12_U_01_HashChain_EachEntryLinksToPrevious()
    {
        await GenerateSomeAuditTrafficAsync();
        var chain = await ChainAsync();
        chain.Should().HaveCountGreaterThanOrEqualTo(3);

        for (var i = 0; i < chain.Count; i++)
        {
            var hash = (string)Planned.Get(chain[i], "Hash")!;
            hash.Should().MatchRegex("^[0-9a-fA-F]{64}$");
            Convert.ToInt64(Planned.Get(chain[i], "Sequence")).Should().Be(Convert.ToInt64(Planned.Get(chain[0], "Sequence")) + i, "sequence has no gaps");
            if (i > 0) Planned.Get(chain[i], "PrevHash").Should().Be(Planned.Get(chain[i - 1], "Hash"));
        }
    }

    [Fact]
    public async Task M12_U_02_Verify_DetectsModifiedRow_AtThatRow()
    {
        await GenerateSomeAuditTrafficAsync();
        var row2 = (await ChainAsync())[1];

        await _host.WithDbAsync(async db =>
        {
            var tracked = await db.AuditLogs.SingleAsync(l => l.LogId == row2.LogId);
            tracked.Details = "{\"tampered\":true}";
            await db.SaveChangesAsync();
        });

        var verify = await VerifyAsync();
        verify.GetProperty("valid").GetBoolean().Should().BeFalse();
        verify.GetProperty("brokenAt").GetInt64().Should().Be(Convert.ToInt64(Planned.Get(row2, "Sequence")));
    }

    [Fact]
    public async Task M12_U_03_Verify_DetectsDeletedRow_AtNextRow()
    {
        await GenerateSomeAuditTrafficAsync();
        var chain = await ChainAsync();
        var (row2, row3) = (chain[1], chain[2]);

        await _host.WithDbAsync(async db =>
        {
            db.AuditLogs.Remove(await db.AuditLogs.SingleAsync(l => l.LogId == row2.LogId));
            await db.SaveChangesAsync();
        });

        var verify = await VerifyAsync();
        verify.GetProperty("valid").GetBoolean().Should().BeFalse();
        verify.GetProperty("brokenAt").GetInt64().Should().Be(Convert.ToInt64(Planned.Get(row3, "Sequence")), "sequence gap is reported at the next row");
    }

    // ── I rows ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task M12_I_01_VerifyCleanChain_ReturnsValidTrue()
    {
        await GenerateSomeAuditTrafficAsync();
        var verify = await VerifyAsync();
        verify.GetProperty("valid").GetBoolean().Should().BeTrue();

        (await (await _host.AsTeacher()).GetAsync("/api/audit-logs/verify")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task M12_I_02_VerifyAfterTamper_ReturnsValidFalse_WithBrokenAt()
    {
        await GenerateSomeAuditTrafficAsync();
        await _host.WithDbAsync(async db =>
        {
            var victim = await db.AuditLogs.OrderBy(l => l.CreatedAt).Skip(1).FirstAsync();
            victim.EventType = "NOTHING_TO_SEE";
            await db.SaveChangesAsync();
        });

        var verify = await VerifyAsync();
        verify.GetProperty("valid").GetBoolean().Should().BeFalse();
        verify.TryGetProperty("brokenAt", out var brokenAt).Should().BeTrue();
        brokenAt.ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task M12_I_03_FiftyParallelAuditWrites_ChainStillValid()
    {
        var tasks = Enumerable.Range(0, 50).Select(i =>
            _host.NewClient().PostAsJsonAsync("/api/auth/login", new { email = SeededData.AdminEmail, password = $"bad-{i}" }));
        (await Task.WhenAll(tasks)).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);

        (await _host.AuditLogsAsync("LOGIN_FAILED")).Should().HaveCount(50);
        (await VerifyAsync()).GetProperty("valid").GetBoolean().Should().BeTrue();
        (await ChainAsync()).Select(l => Planned.Get(l, "Sequence")).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task M12_I_04_AttendanceReport_IsPdf()
    {
        var pdf = await GenerateReportAsync("attendance", "application/pdf");
        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task M12_I_05_ViolationsReport_IsPdf()
    {
        await SubmittedSessionWithViolationAsync();
        var pdf = await GenerateReportAsync("violations", "application/pdf");
        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task M12_I_06_ScoresExcel_HasOneRowPerStudent()
    {
        await SubmittedSessionWithViolationAsync();
        var xlsx = await GenerateReportAsync("excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        using var zip = new ZipArchive(new MemoryStream(xlsx));
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
        sheet.Should().NotBeNull();
        using var reader = new StreamReader(sheet!.Open());
        var xml = await reader.ReadToEndAsync();
        // <row> or a namespace-prefixed <x:row> (ClosedXML writes the SpreadsheetML namespace with a prefix).
        var rows = System.Text.RegularExpressions.Regex.Matches(xml, @"<(\w+:)?row[ >]").Count;
        rows.Should().Be(1 + 2, "header + one row per assigned student");
    }

    [Fact]
    public async Task M12_I_07_EvidenceZip_ManifestHashesMatchFiles()
    {
        await SubmittedSessionWithViolationAsync();
        var bytes = await GenerateReportAsync("evidence-zip", "application/zip");

        using var zip = new ZipArchive(new MemoryStream(bytes));
        var manifestEntry = zip.GetEntry("manifest.json");
        manifestEntry.Should().NotBeNull();
        using var mr = new StreamReader(manifestEntry!.Open());
        var manifest = JsonDocument.Parse(await mr.ReadToEndAsync()).RootElement;

        var files = manifest.GetProperty("files").EnumerateArray().ToList();
        files.Should().NotBeEmpty();
        files.Select(f => f.GetProperty("path").GetString()).Should().Contain(p => p!.EndsWith(".csv"), "events CSV is included");
        foreach (var f in files)
        {
            var entry = zip.GetEntry(f.GetProperty("path").GetString()!);
            entry.Should().NotBeNull();
            using var s = entry!.Open();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            Convert.ToHexString(SHA256.HashData(ms.ToArray())).Should().BeEquivalentTo(f.GetProperty("sha256").GetString());
        }
    }

    [Fact]
    public async Task M12_I_08_PostedViolation_IsVisibleInAuditLog()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.PostEventAsync(student, sid, "Violation", "{\"switchedTo\":\"chrome.exe\"}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await _host.AuditLogsAsync();
        logs.Should().Contain(l => l.GetProperty("eventType").GetString()!.Contains("VIOLATION")
                                   && l.GetRawText().Contains(sid.ToString()));
    }

    [Fact]
    public async Task M12_I_09_Backfill_ExistingRows_ProducesValidChain()
    {
        // Rows written before the migration have no Sequence/Hash.
        await _host.WithDbAsync(async db =>
        {
            for (var i = 0; i < 5; i++)
                db.AuditLogs.Add(new AuditLog { EventType = "LEGACY_EVENT", EntityType = "System", Details = $"{{\"i\":{i}}}", CreatedAt = DateTime.UtcNow.AddDays(-30 + i) });
            await db.SaveChangesAsync();
        });

        (await (await _host.AsAdmin()).PostAsync("/api/audit-logs/backfill", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await VerifyAsync()).GetProperty("valid").GetBoolean().Should().BeTrue();
        (await ChainAsync()).Should().OnlyContain(l => !string.IsNullOrEmpty((string?)Planned.Get(l, "Hash")));
    }

    // ── Added in the Module 12 implementation (beyond the original catalogue rows) ──

    [Fact]
    public void M12_U_04_CanonicalJson_KeyOrderDoesNotChangeTheHash()
    {
        AuditChain.CanonicalJson("{\"b\":1,\"a\":{\"y\":[1,{\"q\":2,\"p\":3}],\"x\":\"s\"}}")
            .Should().Be(AuditChain.CanonicalJson("{ \"a\": { \"x\": \"s\", \"y\": [1, { \"p\": 3, \"q\": 2 }] }, \"b\": 1 }"));

        var at = new DateTime(2026, 10, 21, 9, 30, 15, DateTimeKind.Utc).AddTicks(1234567);
        AuditLog Row(string details) => new()
        {
            LogId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), ActorId = null, EventType = "E", EntityType = "T",
            EntityId = "1", Details = details, CreatedAt = at
        };
        var h1 = AuditChain.ComputeHash(Row("{\"b\":1,\"a\":2}"), 7, AuditChain.GenesisHash);
        AuditChain.ComputeHash(Row("{\"a\":2, \"b\":1}"), 7, AuditChain.GenesisHash).Should().Be(h1);
        AuditChain.ComputeHash(Row("{\"a\":3,\"b\":1}"), 7, AuditChain.GenesisHash).Should().NotBe(h1, "a changed value changes the hash");
        AuditChain.ComputeHash(Row("{\"a\":2,\"b\":1}"), 8, AuditChain.GenesisHash).Should().NotBe(h1, "the sequence is hashed");

        // Hashed at microsecond precision (what PostgreSQL keeps), so a read-back row verifies.
        var stored = Row("{}");
        stored.CreatedAt = AuditChain.NormalizeTimestamp(at);
        AuditChain.ComputeHash(stored, 1, AuditChain.GenesisHash).Should().Be(AuditChain.ComputeHash(Row("{}"), 1, AuditChain.GenesisHash));
    }

    [Fact]
    public async Task M12_U_05_Hash_IsSha256OverPrevHashAndFields_GenesisIsZeros()
    {
        await GenerateSomeAuditTrafficAsync();
        var chain = await ChainAsync();
        chain[0].Sequence.Should().Be(1);
        chain[0].PrevHash.Should().Be(AuditChain.GenesisHash);
        foreach (var row in chain)
            row.Hash.Should().Be(AuditChain.ComputeHash(row, row.Sequence!.Value, row.PrevHash!));
    }

    [Fact]
    public async Task M12_I_10_LegacyRowsWithoutHash_VerifyReportsUnsealed_BackfillSealsOnce()
    {
        await GenerateSomeAuditTrafficAsync();
        // The state right after the migration: rows exist with no chain columns.
        await _host.WithDbAsync(async db =>
        {
            foreach (var row in await db.AuditLogs.ToListAsync())
                (row.Sequence, row.PrevHash, row.Hash) = (null, null, null);
            await db.SaveChangesAsync();
        });
        var legacy = await _host.WithDbAsync(db => db.AuditLogs.CountAsync());

        var before = await VerifyAsync();   // its own login is chained from sequence 1
        before.GetProperty("valid").GetBoolean().Should().BeFalse();
        before.GetProperty("unsealed").GetInt32().Should().Be(legacy);
        before.GetProperty("brokenAt").ValueKind.Should().Be(JsonValueKind.Null, "the sealed part of the chain is intact");

        var admin = await _host.AsAdmin();
        var backfill = await TestHost.DataAsync(await admin.PostAsync("/api/audit-logs/backfill", null));
        backfill.GetProperty("sealed").GetInt32().Should().Be(legacy);
        backfill.GetProperty("valid").GetBoolean().Should().BeTrue();

        (await TestHost.DataAsync(await admin.PostAsync("/api/audit-logs/backfill", null))).GetProperty("sealed").GetInt32().Should().Be(0);
        (await _host.AuditLogsAsync("AUDIT_CHAIN_BACKFILLED")).Should().ContainSingle();
        (await VerifyAsync()).GetProperty("valid").GetBoolean().Should().BeTrue();

        (await (await _host.AsTeacher()).PostAsync("/api/audit-logs/backfill", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task M12_I_11_DeletingAUser_KeepsTheirAuditRows_AndTheChainValid()
    {
        var (userId, email, password) = await _host.CreateUserAsync("Teacher");
        await _host.LoginAsync(email, password);
        (await (await _host.AsAdmin()).DeleteAsync($"/api/users/{userId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await VerifyAsync()).GetProperty("valid").GetBoolean().Should().BeTrue();
        var theirs = (await _host.AuditLogsAsync("LOGIN_SUCCESS")).Where(l => l.GetProperty("actorId").GetString() == userId.ToString()).ToList();
        theirs.Should().NotBeEmpty("the deleted user's id stays on their rows");
        theirs.Should().OnlyContain(l => l.GetProperty("actorName").GetString() == "Deleted user");
    }

    [Fact]
    public async Task M12_I_12_BatchedViolations_AuditedOncePerBatch_WithCount()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        var batch = new
        {
            examSessionId = sid,
            events = new object[]
            {
                new { clientEventId = Guid.NewGuid(), eventType = "FocusLost", payloadJson = "{}", recordedAt = DateTime.UtcNow },
                new { clientEventId = Guid.NewGuid(), eventType = "WindowFocus", payloadJson = "{}", recordedAt = DateTime.UtcNow },
                new { clientEventId = Guid.NewGuid(), eventType = "Violation", payloadJson = "{\"reason\":\"ProcessKilled\"}", recordedAt = DateTime.UtcNow }
            }
        };
        (await student.PostAsJsonAsync("/api/monitoring/events/batch", batch)).StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = (await _host.AuditLogsAsync("MONITORING_VIOLATION")).Where(l => l.GetProperty("entityId").GetString() == sid.ToString()).ToList();
        rows.Should().ContainSingle();
        var details = JsonDocument.Parse(rows[0].GetProperty("details").GetString()!).RootElement;
        details.GetProperty("count").GetInt32().Should().Be(2, "only the violations of the batch");
        details.GetProperty("eventIds").GetArrayLength().Should().Be(2);
    }

    // 1x1 PNG (decodable) and a JPEG header that is not a decodable image: the PDF draws the first and skips the second.
    private static readonly byte[] Png1x1 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    private static readonly byte[] BrokenJpeg =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];

    private static Task<HttpResponseMessage> UploadAsync(HttpClient student, Guid sid, byte[] bytes, string contentType, string trigger)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(sid.ToString()), "examSessionId");
        form.Add(new StringContent("Screen"), "kind");
        form.Add(new StringContent(trigger), "trigger");
        form.Add(new StringContent(DateTime.UtcNow.ToString("O")), "capturedAt");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", contentType == "image/png" ? "screen.png" : "screen.jpg");
        return student.PostAsync("/api/monitoring/screenshot", form);
    }

    [Fact]
    public async Task M12_I_13_Screenshots_InEvidenceZip_AndViolationsPdfCopesWithUndecodableImage()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.PostEventAsync(student, sid, "Violation", "{\"reason\":\"ProcessKilled\"}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadAsync(student, sid, Png1x1, "image/png", "Violation")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadAsync(student, sid, BrokenJpeg, "image/jpeg", "Interval")).StatusCode.Should().Be(HttpStatusCode.OK);

        var pdf = await GenerateReportAsync("violations", "application/pdf");
        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
        Encoding.Latin1.GetString(pdf).Should().Contain("/Subtype /Image", "the decodable capture is drawn as a thumbnail");

        var bytes = await GenerateReportAsync("evidence-zip", "application/zip");
        using var zip = new ZipArchive(new MemoryStream(bytes));
        zip.Entries.Count(e => e.FullName.StartsWith("screenshots/")).Should().Be(2);
        using var mr = new StreamReader(zip.GetEntry("manifest.json")!.Open());
        var manifest = JsonDocument.Parse(await mr.ReadToEndAsync()).RootElement;
        manifest.GetProperty("files").EnumerateArray()
            .Where(f => f.GetProperty("path").GetString()!.StartsWith("screenshots/"))
            .Should().OnlyContain(f => f.GetProperty("source").GetProperty("matchesUpload").GetBoolean());
        manifest.GetProperty("auditChain").GetProperty("valid").GetBoolean().Should().BeTrue();

        using var audit = new StreamReader(zip.GetEntry("audit.csv")!.Open());
        (await audit.ReadToEndAsync()).Should().Contain("MONITORING_VIOLATION").And.Contain(sid.ToString());
    }

    [Fact]
    public async Task M12_A_05_ReportExports_OwnerOnly_UnknownType400_Audited()
    {
        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        var outsider = await _host.LoginAsync(email, password);
        (await outsider.PostAsJsonAsync("/api/analytics/reports/attendance", new { examId = _host.Seed.ExamId }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await _host.AsStudent()).PostAsJsonAsync("/api/analytics/reports/excel", new { examId = _host.Seed.ExamId }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var teacher = await _host.AsTeacher();
        (await teacher.PostAsJsonAsync("/api/analytics/reports/payroll", new { examId = _host.Seed.ExamId }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await teacher.PostAsJsonAsync("/api/analytics/reports/scores", new { examId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var pdf = await GenerateReportAsync("scores", "application/pdf");
        var logged = (await _host.AuditLogsAsync("REPORT_GENERATED")).Should().ContainSingle().Subject;
        JsonDocument.Parse(logged.GetProperty("details").GetString()!).RootElement.GetProperty("sha256").GetString()
            .Should().Be(Convert.ToHexString(SHA256.HashData(pdf)).ToLowerInvariant(), "the audit row vouches for the exact file handed out");

        var history = await TestHost.JsonAsync(await teacher.GetAsync($"/api/analytics/exams/{_host.Seed.ExamId}/reports"));
        history.EnumerateArray().Select(r => r.GetProperty("reportType").GetString()).Should().Contain("Scores");
    }
}
