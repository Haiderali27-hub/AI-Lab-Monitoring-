using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// SEC — security hardening acceptance rows (TEST_CASES.md §SEC).
/// SEC-U-01 → SecurityHardeningTests.HashPassword_UsesPbkdf2_AndVerifies
/// SEC-U-02 → SecurityHardeningTests.VerifyPassword_AcceptsLegacySha256Hash_AndFlagsRehash
/// SEC-I-01 → SecurityHardeningTests.Login_WithLegacyHash_UpgradesStoredHashToPbkdf2
/// SEC-I-02/03 → SecurityHardeningTests.InProduction_SwaggerAndResetDemoData_AreNotExposed
/// </summary>
public class SecurityAcceptanceTests
{
    // SEC-I-04 (replaces SEC-A-01, which can't run reliably in Playwright): the per-client login limiter.
    [Fact]
    public async Task SEC_I_04_LoginRateLimit_FourthFailedLoginFromSameClient_Returns429()
    {
        using var host = new TestHost(b => b.UseSetting("RateLimiting:LoginPermitLimit", "3"));
        var client = host.NewClient().FromIp("10.20.30.40");

        for (var i = 1; i <= 3; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/auth/login", new { email = SeededData.AdminEmail, password = $"wrong-{i}" });
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"attempt {i} is within the limit");
        }

        var fourth = await client.PostAsJsonAsync("/api/auth/login", new { email = SeededData.AdminEmail, password = "wrong-4" });
        fourth.StatusCode.Should().Be((HttpStatusCode)429);

        // Even the correct password is throttled for this client inside the window...
        var correct = await client.PostAsJsonAsync("/api/auth/login", new { email = SeededData.AdminEmail, password = SeededData.AdminPassword });
        correct.StatusCode.Should().Be((HttpStatusCode)429);

        // ...but a different client (IP) has its own budget.
        var other = host.NewClient().FromIp("10.20.30.41");
        (await other.PostAsJsonAsync("/api/auth/login", new { email = SeededData.AdminEmail, password = SeededData.AdminPassword }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
