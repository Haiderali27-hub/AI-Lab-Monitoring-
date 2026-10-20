using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend_API.Models;
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

    [Fact(Skip = "Pending: M12-U-01 — audit hash chain")]
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

    [Fact(Skip = "Pending: M12-U-02 — verify detects a modified row")]
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

    [Fact(Skip = "Pending: M12-U-03 — verify detects a deleted row")]
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

    [Fact(Skip = "Pending: M12-I-01 — audit chain verify endpoint")]
    public async Task M12_I_01_VerifyCleanChain_ReturnsValidTrue()
    {
        await GenerateSomeAuditTrafficAsync();
        var verify = await VerifyAsync();
        verify.GetProperty("valid").GetBoolean().Should().BeTrue();

        (await (await _host.AsTeacher()).GetAsync("/api/audit-logs/verify")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(Skip = "Pending: M12-I-02 — audit chain verify endpoint (tampered)")]
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

    [Fact(Skip = "Pending: M12-I-03 — serialized audit writer keeps chain under concurrency")]
    public async Task M12_I_03_FiftyParallelAuditWrites_ChainStillValid()
    {
        var tasks = Enumerable.Range(0, 50).Select(i =>
            _host.NewClient().PostAsJsonAsync("/api/auth/login", new { email = SeededData.AdminEmail, password = $"bad-{i}" }));
        (await Task.WhenAll(tasks)).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Unauthorized);

        (await _host.AuditLogsAsync("LOGIN_FAILED")).Should().HaveCount(50);
        (await VerifyAsync()).GetProperty("valid").GetBoolean().Should().BeTrue();
        (await ChainAsync()).Select(l => Planned.Get(l, "Sequence")).Should().OnlyHaveUniqueItems();
    }

    [Fact(Skip = "Pending: M12-I-04 — attendance report")]
    public async Task M12_I_04_AttendanceReport_IsPdf()
    {
        var pdf = await GenerateReportAsync("attendance", "application/pdf");
        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact(Skip = "Pending: M12-I-05 — violations report with evidence")]
    public async Task M12_I_05_ViolationsReport_IsPdf()
    {
        await SubmittedSessionWithViolationAsync();
        var pdf = await GenerateReportAsync("violations", "application/pdf");
        Encoding.ASCII.GetString(pdf, 0, 4).Should().Be("%PDF");
    }

    [Fact(Skip = "Pending: M12-I-06 — scores Excel export")]
    public async Task M12_I_06_ScoresExcel_HasOneRowPerStudent()
    {
        await SubmittedSessionWithViolationAsync();
        var xlsx = await GenerateReportAsync("excel", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        using var zip = new ZipArchive(new MemoryStream(xlsx));
        var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
        sheet.Should().NotBeNull();
        using var reader = new StreamReader(sheet!.Open());
        var xml = await reader.ReadToEndAsync();
        var rows = System.Text.RegularExpressions.Regex.Matches(xml, "<row[ >]").Count;
        rows.Should().Be(1 + 2, "header + one row per assigned student");
    }

    [Fact(Skip = "Pending: M12-I-07 — evidence ZIP with manifest")]
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

    [Fact(Skip = "Pending: M12-I-08 — monitoring/app-switch events audited")]
    public async Task M12_I_08_PostedViolation_IsVisibleInAuditLog()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.PostEventAsync(student, sid, "Violation", "{\"switchedTo\":\"chrome.exe\"}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var logs = await _host.AuditLogsAsync();
        logs.Should().Contain(l => l.GetProperty("eventType").GetString()!.Contains("VIOLATION")
                                   && l.GetRawText().Contains(sid.ToString()));
    }

    [Fact(Skip = "Pending: M12-I-09 — backfill hash chain over pre-existing rows")]
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
}
