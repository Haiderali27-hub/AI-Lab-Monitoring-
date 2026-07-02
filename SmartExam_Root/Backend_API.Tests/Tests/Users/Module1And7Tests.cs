using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Users;

/// <summary>
/// Module 1 (Auth &amp; HWID) + Module 7 (User &amp; Role Management) hardening.
/// Covers: audit logging, HWID hashing (raw never stored), session-revocation enforcement,
/// deactivation enforcement, change-password, edit/delete/activate user, role change,
/// batch import, Invigilator role — with negative cases.
/// Uses a fresh factory per test (IDisposable) so revocation/deactivation don't leak.
/// </summary>
public class Module1And7Tests : IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public Module1And7Tests()
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

    private static async Task<string> LoginToken(HttpClient c, string email, string password)
    {
        var resp = await c.PostAsJsonAsync("/api/auth/login", new { email, password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    // ══════════════ MODULE 1 — Auth & HWID ══════════════

    [Fact]
    public async Task Login_WritesAuditLog_AndAdminCanReadIt()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var resp = await _client.GetAsync("/api/audit-logs?eventType=LOGIN_SUCCESS");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var logs = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();
        logs!.Should().NotBeEmpty();
        logs![0].GetProperty("eventType").GetString().Should().Be("LOGIN_SUCCESS");
    }

    [Fact]
    public async Task DeviceMismatch_IsAudited()
    {
        await _client.PostAsJsonAsync("/api/auth/student-login", new
        {
            usernameOrEmail = SeededData.StudentEmail,
            password = SeededData.StudentPassword,
            hardwareFingerprint = SeededData.WrongHwid
        });

        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var logs = await (await _client.GetAsync("/api/audit-logs?eventType=DEVICE_MISMATCH")).Content.ReadFromJsonAsync<List<JsonElement>>();
        logs!.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HwidIsStoredHashed_NotRaw()
    {
        // A fresh student binds with a known raw fingerprint
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Backend_API.Data.AppDbContext>();
        // The seeded student's binding must not equal the raw test HWID (it is hashed)
        var binding = db.DeviceBindings.First(b => b.UserId == _seed.StudentId);
        binding.HwidHash.Should().NotBe(SeededData.TestHwid);
        binding.HwidHash.Should().Be(Backend_API.Helpers.HwidHelper.Hash(SeededData.TestHwid));
    }

    [Fact]
    public async Task RevokedToken_IsRejected_AfterLogout()
    {
        var token = await LoginToken(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // token works
        (await _client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        // logout revokes this session
        (await _client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        // same token now rejected
        (await _client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeactivatedUser_ExistingToken_IsRejected()
    {
        // Teacher logs in and gets a working token
        var token = await LoginToken(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var teacherClient = _factory.CreateClient();
        teacherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await teacherClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Admin deactivates the teacher
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        (await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/users/{_seed.TeacherId}/deactivate")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Teacher's still-unexpired token is now rejected, and re-login is blocked
        (await teacherClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync("/api/auth/login", new { email = SeededData.TeacherEmail, password = SeededData.TeacherPassword }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ChangePassword_WithCorrectCurrent_Works_AndOldPasswordFails()
    {
        // Create a throwaway user so we don't disturb shared seed creds
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var email = $"pwtest_{Guid.NewGuid()}@test.com";
        await _client.PostAsJsonAsync("/api/users", new { name = "PW Test", email, password = "OldPass@123", role = "Teacher" });

        var token = await LoginToken(_client, email, "OldPass@123");
        var userClient = _factory.CreateClient();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var changeResp = await userClient.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "OldPass@123", newPassword = "NewPass@456" });
        changeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // old password rejected, new password works
        (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "OldPass@123" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "NewPass@456" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ChangePassword_WithWrongCurrent_Returns400()
    {
        var token = await LoginToken(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/auth/change-password",
            new { currentPassword = "TotallyWrong", newPassword = "NewPass@456" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuditLogs_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        (await _client.GetAsync("/api/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ══════════════ MODULE 7 — User & Role Management ══════════════

    [Fact]
    public async Task UpdateUser_ChangesNameEmailRole()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var email = $"edit_{Guid.NewGuid()}@test.com";
        var create = await _client.PostAsJsonAsync("/api/users", new { name = "Before", email, password = "Pass@123", role = "Student" });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString();

        var newEmail = $"edited_{Guid.NewGuid()}@test.com";
        var upd = await _client.PutAsJsonAsync($"/api/users/{id}", new { name = "After", email = newEmail, role = "Invigilator" });
        upd.StatusCode.Should().Be(HttpStatusCode.OK);

        var got = await (await _client.GetAsync($"/api/users/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        got.GetProperty("name").GetString().Should().Be("After");
        got.GetProperty("email").GetString().Should().Be(newEmail);
        got.GetProperty("role").GetString().Should().Be("Invigilator");
    }

    [Fact]
    public async Task UpdateUser_ToDuplicateEmail_Returns409()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var create = await _client.PostAsJsonAsync("/api/users", new { name = "X", email = $"x_{Guid.NewGuid()}@test.com", password = "Pass@123", role = "Student" });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString();

        // Try to take the seeded teacher's email
        var upd = await _client.PutAsJsonAsync($"/api/users/{id}", new { name = "X", email = SeededData.TeacherEmail, role = "Student" });
        upd.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteUser_NoDependencies_Works()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var create = await _client.PostAsJsonAsync("/api/users", new { name = "Deletable", email = $"del_{Guid.NewGuid()}@test.com", password = "Pass@123", role = "Student" });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString();

        (await _client.DeleteAsync($"/api/users/{id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync($"/api/users/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteUser_WhoOwnsSections_Returns409()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        // Seeded teacher owns the seeded section
        (await _client.DeleteAsync($"/api/users/{_seed.TeacherId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteUser_Self_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        (await _client.DeleteAsync($"/api/users/{_seed.AdminId}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeactivateThenActivate_TogglesAndRestoresLogin()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var email = $"toggle_{Guid.NewGuid()}@test.com";
        var create = await _client.PostAsJsonAsync("/api/users", new { name = "Toggle", email, password = "Pass@123", role = "Teacher" });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetString();

        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/users/{id}/deactivate"));
        (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Pass@123" }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await _client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/api/users/{id}/activate"));
        (await _client.PostAsJsonAsync("/api/auth/login", new { email, password = "Pass@123" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ImportUsers_CreatesValid_SkipsDuplicates()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var unique = Guid.NewGuid().ToString("N")[..6];
        var payload = new
        {
            users = new object[]
            {
                new { name = "Imp One", email = $"imp1_{unique}@test.com", password = "Pass@123", role = "Student" },
                new { name = "Imp Two", email = $"imp2_{unique}@test.com", password = "Pass@123", role = "Student" },
                new { name = "Dup Teacher", email = SeededData.TeacherEmail, password = "Pass@123", role = "Teacher" }, // existing → skipped
                new { name = "Blank", email = "", password = "Pass@123", role = "Student" } // invalid → skipped
            }
        };
        var resp = await _client.PostAsJsonAsync("/api/users/import", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("created").GetInt32().Should().Be(2);
        body.GetProperty("skippedCount").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ImportUsers_AsTeacher_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var payload = new { users = new object[] { new { name = "N", email = "n@test.com", password = "Pass@123", role = "Student" } } };
        (await _client.PostAsJsonAsync("/api/users/import", payload)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateUser_WithInvigilatorRole_Works()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var resp = await _client.PostAsJsonAsync("/api/users", new { name = "Invig", email = $"invig_{Guid.NewGuid()}@test.com", password = "Pass@123", role = "Invigilator" });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("role").GetString().Should().Be("Invigilator");
    }
}
