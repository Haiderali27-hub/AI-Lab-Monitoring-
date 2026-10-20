using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend_API.Data;
using Backend_API.Helpers;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend_API.Tests.Tests.Security;

public class SecurityHardeningTests : IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public SecurityHardeningTests()
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

    private static string LegacySha256(string password, string salt) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));

    // ── Password hashing ──────────────────────────────────────────────────────

    [Fact]
    public void HashPassword_UsesPbkdf2_AndVerifies()
    {
        var salt = PasswordHelper.GenerateSalt();
        var hash = PasswordHelper.HashPassword("Correct#Horse1", salt);

        hash.Should().StartWith("pbkdf2$");
        PasswordHelper.VerifyPassword("Correct#Horse1", salt, hash).Should().BeTrue();
        PasswordHelper.VerifyPassword("wrong", salt, hash).Should().BeFalse();
        PasswordHelper.NeedsRehash(hash).Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_AcceptsLegacySha256Hash_AndFlagsRehash()
    {
        var salt = PasswordHelper.GenerateSalt();
        var legacy = LegacySha256("Admin@123", salt);

        PasswordHelper.VerifyPassword("Admin@123", salt, legacy).Should().BeTrue();
        PasswordHelper.VerifyPassword("Admin@124", salt, legacy).Should().BeFalse();
        PasswordHelper.NeedsRehash(legacy).Should().BeTrue();
    }

    [Fact]
    public async Task Login_WithLegacyHash_UpgradesStoredHashToPbkdf2()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.SingleAsync(u => u.Email == SeededData.AdminEmail);
            admin.PasswordHash = LegacySha256(SeededData.AdminPassword, admin.Salt);
            await db.SaveChangesAsync();
        }

        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var admin = await db.Users.AsNoTracking().SingleAsync(u => u.Email == SeededData.AdminEmail);
            admin.PasswordHash.Should().StartWith("pbkdf2$");
            PasswordHelper.VerifyPassword(SeededData.AdminPassword, admin.Salt, admin.PasswordHash).Should().BeTrue();
        }
    }

    // ── Server-side exam deadline ─────────────────────────────────────────────

    private async Task<string> StartSessionAsStudent()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var resp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString()!;
    }

    [Fact]
    public async Task SaveAnswer_AfterExamDeadline_Returns400()
    {
        var sessionId = await StartSessionAsStudent();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exam = await db.Exams.SingleAsync(e => e.ExamId == _seed.ExamId);
            exam.StartTime = DateTime.UtcNow.AddMinutes(-(exam.DurationMinutes + 10));
            await db.SaveChangesAsync();
        }

        var save = await _client.PostAsJsonAsync($"/api/exams/sessions/{sessionId}/save-answer",
            new { questionId = _seed.Question1Id, answerText = "late answer" });

        save.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForceSubmitAll_GradesTheForceSubmittedAnswers()
    {
        var sessionId = await StartSessionAsStudent();
        var save = await _client.PostAsJsonAsync($"/api/exams/sessions/{sessionId}/save-answer",
            new { questionId = _seed.Question1Id, answerText = "for (int i = 0; i < n; i++) max = arr[i];" });
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var force = await _client.PostAsync($"/api/exams/{_seed.ExamId}/force-submit-all", null);
        force.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var grading = await db.AiGradingResults
            .Where(g => g.Answer.SessionId == Guid.Parse(sessionId))
            .ToListAsync();
        grading.Should().NotBeEmpty();
        grading.Should().OnlyContain(g => g.Confidence == "Low" && g.Justification.Contains("teacher review"));
    }

    // ── Exam detail must not leak credentials or other exams (SEC-A-06) ───────

    [Fact]
    public async Task GetExamById_AsTeacher_NeverIncludesPasswordHashOrSalt()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var resp = await _client.GetAsync($"/api/exams/{_seed.ExamId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadAsStringAsync();
        json.Should().NotContainEquivalentOf("passwordHash").And.NotContainEquivalentOf("\"salt\"");
        // The edit page still needs the assigned student ids.
        var body = JsonDocument.Parse(json).RootElement;
        body.GetProperty("assignments").EnumerateArray().Should().NotBeEmpty();
        body.GetProperty("assignments")[0].TryGetProperty("userId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task GetExamById_AsStudentNotAssigned_Returns404()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var student2 = await db.Users.SingleAsync(u => u.Email == SeededData.Student2Email);
            db.ExamAssignments.RemoveRange(db.ExamAssignments.Where(a => a.UserId == student2.UserId));
            await db.SaveChangesAsync();
        }

        await AuthHelper.AuthorizeAs(_client, SeededData.Student2Email, SeededData.Student2Password, SeededData.TestHwid2);
        (await _client.GetAsync($"/api/exams/{_seed.ExamId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetExamById_AsAssignedStudent_HidesAnswerKeyAndAssignments()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var body = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}")).Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("assignments").GetArrayLength().Should().Be(0);
        foreach (var q in body.GetProperty("questions").EnumerateArray())
        {
            q.GetProperty("correctOptionIndex").ValueKind.Should().Be(JsonValueKind.Null);
            q.GetProperty("referenceAnswer").ValueKind.Should().Be(JsonValueKind.Null);
            q.GetProperty("testCases").EnumerateArray().Where(tc => tc.GetProperty("isHidden").GetBoolean()).Should().BeEmpty();
        }
    }

    // ── Desktop start path activates the exam (live monitor bug) ──────────────

    [Fact]
    public async Task StudentStart_FromDesktopClient_ActivatesScheduledExam()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        (await _client.PostAsync("/api/exams/student/start", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Exams.SingleAsync(e => e.ExamId == _seed.ExamId)).Status.Should().Be(Backend_API.Models.Enums.ExamStatus.Active);
    }

    // ── Production defaults ───────────────────────────────────────────────────

    [Fact]
    public async Task InProduction_SwaggerAndResetDemoData_AreNotExposed()
    {
        using var prod = _factory.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var client = prod.CreateClient();

        (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await AuthHelper.AuthorizeAs(client, SeededData.AdminEmail, SeededData.AdminPassword);
        (await client.PostAsync("/api/admin/reset-demo-data", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
