using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Services;
using Backend_API.Tests.Helpers;
using Backend_API.Tests.Tests.Acceptance;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Auth;

/// <summary>
/// Module 1 hardening beyond the M1-I-* acceptance contract: refresh-token revocation paths,
/// absolute family expiry, hashing at rest, desktop seat claims surviving a refresh, roll-number
/// management and the HWID v2 upgrade rule.
/// </summary>
public class RefreshTokenAndRollNumberTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private async Task<JsonElement> StudentLoginAsync(string? machineName = null, string hwid = SeededData.TestHwid)
    {
        var resp = await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword, hwid, machineName);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        _host.NewClient().PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    private static JsonElement Claims(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        return JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;
    }

    private HttpClient Authed(string accessToken)
    {
        var c = _host.NewClient();
        AuthHelper.SetToken(c, accessToken);
        return c;
    }

    // ── Refresh tokens ────────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownOrBlankRefreshToken_Returns401_InvalidRefreshToken()
    {
        var unknown = await RefreshAsync("not-a-real-token");
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TestHost.ErrorCodeAsync(unknown)).Should().Be(RefreshTokenService.InvalidCode);

        (await RefreshAsync("")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshToken_IsStoredOnlyAsHash()
    {
        var raw = (await StudentLoginAsync()).GetProperty("refreshToken").GetString()!;

        var rows = await _host.WithDbAsync(db => db.RefreshTokens.Where(t => t.UserId == _host.Seed.StudentId).ToListAsync());
        rows.Should().ContainSingle();
        rows[0].TokenHash.Should().Be(RefreshTokenService.Hash(raw)).And.NotBe(raw).And.HaveLength(64);
        rows[0].Channel.Should().Be(RefreshTokenChannels.StudentClient);
    }

    [Fact]
    public async Task DesktopRefresh_KeepsClientAndMachineClaims_ForSeatEnforcement()
    {
        var login = await StudentLoginAsync(machineName: "LAB-PC-07");
        var resp = await RefreshAsync(login.GetProperty("refreshToken").GetString()!);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var claims = Claims((await TestHost.DataAsync(resp)).GetProperty("accessToken").GetString()!);
        claims.GetProperty(SeatGuard.ClientClaim).GetString().Should().Be(SeatGuard.DesktopClient);
        claims.GetProperty(SeatGuard.MachineClaim).GetString().Should().Be("LAB-PC-07");
        claims.TryGetProperty("iat", out _).Should().BeTrue();
    }

    [Fact]
    public async Task StaffLogin_IssuesRefreshToken_AndWebRefreshCarriesNoDesktopClaim()
    {
        var login = await _host.NewClient().PostAsJsonAsync("/api/auth/login",
            new { email = SeededData.TeacherEmail, password = SeededData.TeacherPassword });
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        var refresh = body.GetProperty("refreshToken").GetString();
        refresh.Should().NotBeNullOrWhiteSpace();
        body.GetProperty("accessTokenExpiresAtUtc").GetDateTime().Should().BeAfter(DateTime.UtcNow);

        var resp = await RefreshAsync(refresh!);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await TestHost.DataAsync(resp);
        data.GetProperty("user").GetProperty("role").GetString().Should().Be("Teacher");
        Claims(data.GetProperty("accessToken").GetString()!).TryGetProperty(SeatGuard.ClientClaim, out _).Should().BeFalse();

        (await Authed(data.GetProperty("accessToken").GetString()!).GetAsync("/api/exams")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rotation_DoesNotExtendTheFamilyExpiry()
    {
        var r1 = (await StudentLoginAsync()).GetProperty("refreshToken").GetString()!;
        var r2 = (await TestHost.DataAsync(await RefreshAsync(r1))).GetProperty("refreshToken").GetString()!;

        var (first, second) = await _host.WithDbAsync(async db =>
            (await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(r1)),
             await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(r2))));

        second.ExpiresAt.Should().Be(first.ExpiresAt);
        second.FamilyId.Should().Be(first.FamilyId);
        first.RevokedReason.Should().Be(RefreshTokenRevocation.Rotated);
        first.ReplacedByTokenId.Should().Be(second.TokenId);
    }

    [Fact]
    public async Task ExpiredRefreshToken_Returns401_Expired()
    {
        var raw = (await StudentLoginAsync()).GetProperty("refreshToken").GetString()!;
        await _host.WithDbAsync(async db =>
        {
            var row = await db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(raw));
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        });

        var resp = await RefreshAsync(raw);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be(RefreshTokenService.ExpiredCode);
    }

    [Fact]
    public async Task Logout_RevokesTheRefreshTokenFamily()
    {
        var login = await StudentLoginAsync();
        var r1 = login.GetProperty("refreshToken").GetString()!;
        // Rotate once, then log out with the newest access token: the whole family must die.
        var data = await TestHost.DataAsync(await RefreshAsync(r1));
        var access2 = data.GetProperty("accessToken").GetString()!;
        var r2 = data.GetProperty("refreshToken").GetString()!;

        (await Authed(access2).PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(r2)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _host.AuditLogsAsync("REFRESH_TOKEN_REUSE")).Should().BeEmpty("a revoked (not rotated) token is not a theft signal");
    }

    [Fact]
    public async Task ForceLogout_RevokesRefreshTokens()
    {
        var raw = (await StudentLoginAsync()).GetProperty("refreshToken").GetString()!;
        var admin = await _host.AsAdmin();
        (await admin.PostAsync($"/api/users/{_host.Seed.StudentId}/force-logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(raw)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var audit = await _host.AuditLogsAsync("FORCE_LOGOUT");
        audit.Should().ContainSingle().Which.GetProperty("details").GetString().Should().Contain("\"revokedRefreshTokens\":1");
    }

    [Fact]
    public async Task Deactivate_RevokesRefreshTokens()
    {
        var raw = (await StudentLoginAsync()).GetProperty("refreshToken").GetString()!;
        var admin = await _host.AsAdmin();
        (await admin.PatchAsync($"/api/users/{_host.Seed.StudentId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await RefreshAsync(raw)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var row = await _host.WithDbAsync(db => db.RefreshTokens.SingleAsync(t => t.TokenHash == RefreshTokenService.Hash(raw)));
        row.RevokedReason.Should().Be(RefreshTokenRevocation.Deactivated);
    }

    [Fact]
    public async Task Refresh_ForInactiveUserWithLiveToken_IsRefused()
    {
        var raw = (await StudentLoginAsync()).GetProperty("refreshToken").GetString()!;
        // Deactivated directly in the DB (bypassing the endpoint's revocation) — refresh must still refuse.
        await _host.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.UserId == _host.Seed.StudentId)).IsActive = false;
            await db.SaveChangesAsync();
        });

        (await RefreshAsync(raw)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Roll numbers ─────────────────────────────────────────────────────────

    private Task<HttpResponseMessage> SetRollNumberAsync(HttpClient admin, Guid userId, string name, string email, string? roll) =>
        admin.PutAsJsonAsync($"/api/users/{userId}", new { name, email, role = "Student", rollNumber = roll });

    [Fact]
    public async Task RollNumber_IsNormalised_Unique_AndClearable()
    {
        var admin = await _host.AsAdmin();
        (await SetRollNumberAsync(admin, _host.Seed.StudentId, "Test Student", SeededData.StudentEmail, "  bscs-21-001 "))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await (await admin.GetAsync($"/api/users/{_host.Seed.StudentId}")).Content.ReadFromJsonAsync<JsonElement>();
        detail.GetProperty("rollNumber").GetString().Should().Be("BSCS-21-001");

        // Login is case-insensitive on the roll number.
        (await _host.StudentLoginRawAsync(_host.NewClient(), "bscs-21-001", SeededData.StudentPassword, SeededData.TestHwid))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Another user cannot take it (update or create).
        (await SetRollNumberAsync(admin, _host.Seed.Student2Id, "Test Student Two", SeededData.Student2Email, "BSCS-21-001"))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Dup", email = $"dup_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Student", rollNumber = "bscs-21-001"
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Omitting rollNumber keeps it; "" clears it.
        (await admin.PutAsJsonAsync($"/api/users/{_host.Seed.StudentId}", new { name = "Test Student", email = SeededData.StudentEmail, role = "Student" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.WithDbAsync(db => db.Users.Where(u => u.UserId == _host.Seed.StudentId).Select(u => u.RollNumber).SingleAsync()))
            .Should().Be("BSCS-21-001");
        (await SetRollNumberAsync(admin, _host.Seed.StudentId, "Test Student", SeededData.StudentEmail, ""))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.StudentLoginRawAsync(_host.NewClient(), "BSCS-21-001", SeededData.StudentPassword, SeededData.TestHwid))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RollNumber_WithAtSign_IsRejected()
    {
        var admin = await _host.AsAdmin();
        (await SetRollNumberAsync(admin, _host.Seed.StudentId, "Test Student", SeededData.StudentEmail, "a@b"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Import_StoresRollNumbers_AndSkipsDuplicates()
    {
        var admin = await _host.AsAdmin();
        var resp = await admin.PostAsJsonAsync("/api/users/import", new
        {
            users = new object[]
            {
                new { name = "Roll A", email = $"ra_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Student", rollNumber = "r-100" },
                new { name = "Roll B", email = $"rb_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Student", rollNumber = "R-100" },
                new { name = "Roll C", email = $"rc_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Student" }
            }
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("created").GetInt32().Should().Be(2);
        body.GetProperty("skipped")[0].GetProperty("reason").GetString().Should().Be("duplicate roll number");
        (await _host.WithDbAsync(db => db.Users.CountAsync(u => u.RollNumber == "R-100"))).Should().Be(1);
    }

    // ── HWID v2 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstBind_WithV2Fingerprint_StoresVersion2()
    {
        var admin = await _host.AsAdmin();
        (await admin.DeleteAsync($"/api/users/{_host.Seed.StudentId}/device-binding")).StatusCode.Should().Be(HttpStatusCode.OK);

        await StudentLoginAsync(hwid: "v2|BIOS-1|DISK-1|BOARD-1");
        var binding = await _host.WithDbAsync(db => db.DeviceBindings.SingleAsync(d => d.UserId == _host.Seed.StudentId));
        binding.HwidVersion.Should().Be(2);
        binding.HwidHash.Should().Be(HwidHelper.Hash("v2|BIOS-1|DISK-1|BOARD-1"));
    }

    [Fact]
    public async Task V2Upgrade_WithWrongLegacyProof_IsRejected_AndNotRebound()
    {
        var resp = await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword,
            "v2|B|D|M", extra: new { legacyHardwareFingerprint = SeededData.WrongHwid });
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TestHost.ErrorCodeAsync(resp)).Should().Be("DEVICE_MISMATCH");

        // v2 alone is not enough either: the upgrade needs the old fingerprint as proof.
        (await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword, "v2|B|D|M"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await _host.AuditLogsAsync("DEVICE_REBOUND")).Should().BeEmpty();
        var binding = await _host.WithDbAsync(db => db.DeviceBindings.SingleAsync(d => d.UserId == _host.Seed.StudentId));
        binding.HwidVersion.Should().Be(1);
    }

    [Fact]
    public async Task V2Binding_CannotBeRebound_ByAnotherV2Fingerprint()
    {
        await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword,
            "v2|B|D|M", extra: new { legacyHardwareFingerprint = SeededData.TestHwid });

        // The migration path is closed once the binding is v2, even with the old legacy proof.
        var other = await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword,
            "v2|OTHER|PC|X", extra: new { legacyHardwareFingerprint = SeededData.TestHwid });
        other.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TestHost.ErrorCodeAsync(other)).Should().Be("DEVICE_MISMATCH");
    }

    // ── Pure helpers ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "v2|a|b|c", "LEGACY", true)]
    [InlineData(2, "v2|a|b|c", "LEGACY", false)]   // already upgraded
    [InlineData(1, "LEGACY-2", "LEGACY", false)]   // presented fingerprint is not v2
    [InlineData(1, "v2|a|b|c", "OTHER", false)]    // wrong proof
    [InlineData(1, "v2|a|b|c", "v2|x|y|z", false)] // proof must be a legacy fingerprint
    [InlineData(1, "v2|a|b|c", null, false)]
    public void CanUpgradeToV2_Rule(int version, string presented, string? legacy, bool expected) =>
        HwidHelper.CanUpgradeToV2(version, HwidHelper.Hash("LEGACY"), presented, legacy).Should().Be(expected);

    [Fact]
    public void NewRawToken_IsHighEntropy_AndUnique()
    {
        var a = RefreshTokenService.NewRawToken();
        var b = RefreshTokenService.NewRawToken();
        a.Length.Should().Be(86);
        a.Should().NotBe(b);
        a.Should().MatchRegex("^[A-Za-z0-9_-]+$");
    }
}
