using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M1 — Authentication &amp; Device Binding (TEST_CASES.md §M1, layer I).
/// M1-I-08 (deactivated user blocked) → Module1And7Tests.DeactivatedUser_ExistingToken_IsRejected
///   (also Module1And7Tests.DeactivateThenActivate_TogglesAndRestoresLogin).
/// Planned contract: IMPLEMENTATION_PLAN.md §6 (refresh tokens, roll number, HWID v2).
/// </summary>
public class Module1AuthAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private async Task<JsonElement> StudentLoginDataAsync(HttpClient c, string identifier = SeededData.StudentEmail, string hwid = SeededData.TestHwid)
    {
        var resp = await _host.StudentLoginRawAsync(c, identifier, SeededData.StudentPassword, hwid);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient c, string refreshToken) =>
        c.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    [Fact(Skip = "Pending: M1-I-01 — real refresh tokens")]
    public async Task M1_I_01_Login_IssuesNonDummyRefreshToken()
    {
        var c = _host.NewClient();
        var data = await StudentLoginDataAsync(c);
        var refresh = data.GetProperty("refreshToken").GetString();

        refresh.Should().NotBeNullOrWhiteSpace();
        refresh.Should().NotBe("dummy-refresh-token");
        refresh!.Length.Should().BeGreaterThanOrEqualTo(32, "refresh tokens must be high-entropy");

        // A second login yields a different refresh token.
        var again = await StudentLoginDataAsync(_host.NewClient());
        again.GetProperty("refreshToken").GetString().Should().NotBe(refresh);
    }

    [Fact(Skip = "Pending: M1-I-02 — refresh token rotation")]
    public async Task M1_I_02_Refresh_RotatesAccessAndRefreshToken_OldRefreshInvalid()
    {
        var c = _host.NewClient();
        var login = await StudentLoginDataAsync(c);
        var oldAccess = login.GetProperty("accessToken").GetString();
        var oldRefresh = login.GetProperty("refreshToken").GetString()!;

        var resp = await RefreshAsync(c, oldRefresh);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await TestHost.DataAsync(resp);
        var newAccess = data.GetProperty("accessToken").GetString();
        var newRefresh = data.GetProperty("refreshToken").GetString();

        newAccess.Should().NotBeNullOrWhiteSpace().And.NotBe(oldAccess);
        newRefresh.Should().NotBeNullOrWhiteSpace().And.NotBe(oldRefresh);

        // The new access token works.
        var authed = _host.NewClient();
        AuthHelper.SetToken(authed, newAccess!);
        (await authed.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // The old refresh token can no longer be used.
        (await RefreshAsync(_host.NewClient(), oldRefresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(Skip = "Pending: M1-I-03 — refresh token reuse detection")]
    public async Task M1_I_03_RefreshReuse_Returns401_AndRevokesWholeFamily()
    {
        var c = _host.NewClient();
        var r1 = (await StudentLoginDataAsync(c)).GetProperty("refreshToken").GetString()!;

        var rotated = await RefreshAsync(c, r1);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await TestHost.DataAsync(rotated);
        var r2 = data.GetProperty("refreshToken").GetString()!;
        var access2 = data.GetProperty("accessToken").GetString()!;

        // Replay of the already-rotated token = theft signal.
        (await RefreshAsync(_host.NewClient(), r1)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The whole family is now revoked: the legitimate successor no longer refreshes...
        (await RefreshAsync(_host.NewClient(), r2)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // ...and the access token issued from that family is rejected too.
        var authed = _host.NewClient();
        AuthHelper.SetToken(authed, access2);
        (await authed.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await _host.AuditLogsAsync("REFRESH_TOKEN_REUSE")).Should().NotBeEmpty();
    }

    [Fact(Skip = "Pending: M1-I-04 — 30-minute access tokens")]
    public async Task M1_I_04_AccessTokenLifetime_Is30Minutes()
    {
        // The shared test factory pins Jwt:ExpiryMinutes=480; the planned default is a 30-min access token.
        using var host = new TestHost(b => b.UseSetting("Jwt:ExpiryMinutes", "30"));
        var c = host.NewClient();
        var resp = await host.StudentLoginRawAsync(c, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var token = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("accessToken").GetString()!;

        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var claims = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))).RootElement;

        claims.TryGetProperty("iat", out var iat).Should().BeTrue("the access token must carry an issued-at claim");
        var lifetime = claims.GetProperty("exp").GetInt64() - iat.GetInt64();
        lifetime.Should().Be(1800);
    }

    [Fact(Skip = "Pending: M1-I-05 — login by unique roll number")]
    public async Task M1_I_05_StudentLogin_ByRollNumber_Returns200()
    {
        var admin = await _host.AsAdmin();
        var set = await admin.PutAsJsonAsync($"/api/users/{_host.Seed.StudentId}", new
        {
            name = "Test Student", email = SeededData.StudentEmail, role = "Student", rollNumber = "BSCS-21-001"
        });
        set.StatusCode.Should().Be(HttpStatusCode.OK);

        var resp = await _host.StudentLoginRawAsync(_host.NewClient(), "BSCS-21-001", SeededData.StudentPassword, SeededData.TestHwid);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").GetProperty("user")
            .GetProperty("email").GetString().Should().Be(SeededData.StudentEmail);
    }

    [Fact(Skip = "Pending: M1-I-06 — student login must not resolve by (non-unique) display name")]
    public async Task M1_I_06_DuplicateNames_LoginByName_IsRejected()
    {
        // A second active student with exactly the same display name and password as the seeded one.
        var admin = await _host.AsAdmin();
        (await admin.PostAsJsonAsync("/api/users", new
        {
            name = "Test Student", email = $"dup_{Guid.NewGuid():N}@test.com", password = SeededData.StudentPassword, role = "Student"
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var resp = await _host.StudentLoginRawAsync(_host.NewClient(), "Test Student", SeededData.StudentPassword, SeededData.TestHwid);
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);

        // Email still works.
        (await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Skip = "Pending: M1-I-07 — HWID v2 (BIOS+disk+baseboard) migration")]
    public async Task M1_I_07_HwidV2Migration_AcceptedOnce_Rebinds_AndIsAudited()
    {
        const string v2 = "v2|BIOS-SN-1234|DISK-SN-5678|BOARD-SN-9012";

        // Client sends the new-format fingerprint plus the legacy one it used to send.
        var first = await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword, v2,
            extra: new { legacyHardwareFingerprint = SeededData.TestHwid });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        (await _host.AuditLogsAsync("DEVICE_REBOUND")).Should().ContainSingle();

        // Binding is now the v2 hash: v2 alone works, the legacy fingerprint no longer does.
        (await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword, v2))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var legacy = await _host.StudentLoginRawAsync(_host.NewClient(), SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        legacy.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TestHost.ErrorCodeAsync(legacy)).Should().Be("DEVICE_MISMATCH");
    }
}
