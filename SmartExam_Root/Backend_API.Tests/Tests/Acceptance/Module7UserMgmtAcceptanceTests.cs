using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M7 — User &amp; Role Management (TEST_CASES.md §M7, layer I).
/// M7-I-01 (bulk import, bad rows reported) → Module1And7Tests.ImportUsers_CreatesValid_SkipsDuplicates.
/// Planned contract: IMPLEMENTATION_PLAN.md §6 / §4.3 — Departments/Courses CRUD, SuperAdmin-only admin
/// management, ExamProctors (POST /api/exams/{id}/proctors) scoping invigilators.
/// </summary>
public class Module7UserMgmtAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact(Skip = "Pending: M7-I-02 — Departments CRUD API")]
    public async Task M7_I_02_DepartmentsCrud_Works()
    {
        var admin = await _host.AsAdmin();

        var created = await admin.PostAsJsonAsync("/api/departments", new { name = "Electrical Engineering" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await TestHost.DataAsync(created)).GetProperty("deptId").GetGuid();

        var list = await (await admin.GetAsync("/api/departments")).Content.ReadAsStringAsync();
        list.Should().Contain("Electrical Engineering").And.Contain("Computer Science");

        (await admin.PutAsJsonAsync($"/api/departments/{id}", new { name = "Electrical & Computer Eng." })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await admin.GetAsync($"/api/departments/{id}")).Content.ReadAsStringAsync()).Should().Contain("Electrical & Computer Eng.");

        (await admin.DeleteAsync($"/api/departments/{id}")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/departments/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // A department that still owns courses cannot be deleted.
        (await admin.DeleteAsync($"/api/departments/{_host.Seed.DeptId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Teachers can read but not write.
        var teacher = await _host.AsTeacher();
        (await teacher.PostAsJsonAsync("/api/departments", new { name = "Nope" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(Skip = "Pending: M7-I-03 — Courses CRUD API")]
    public async Task M7_I_03_CoursesCrud_Works()
    {
        var admin = await _host.AsAdmin();

        var created = await admin.PostAsJsonAsync("/api/courses", new { deptId = _host.Seed.DeptId, name = "Operating Systems", code = "CS401" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await TestHost.DataAsync(created)).GetProperty("courseId").GetGuid();

        (await (await admin.GetAsync($"/api/courses?deptId={_host.Seed.DeptId}")).Content.ReadAsStringAsync())
            .Should().Contain("CS401").And.Contain("CS301");

        (await admin.PutAsJsonAsync($"/api/courses/{id}", new { deptId = _host.Seed.DeptId, name = "Operating Systems II", code = "CS402" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await admin.GetAsync($"/api/courses/{id}")).Content.ReadAsStringAsync()).Should().Contain("CS402");

        // Duplicate code within a department is rejected.
        (await admin.PostAsJsonAsync("/api/courses", new { deptId = _host.Seed.DeptId, name = "Dup", code = "CS301" }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await admin.DeleteAsync($"/api/courses/{id}")).StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/courses/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Skip = "Pending: M7-I-04 — SuperAdmin-only management of Admin/SuperAdmin accounts")]
    public async Task M7_I_04_SuperAdminCreatesAdmin_AdminCannotCreateSuperAdmin()
    {
        // No API can create the first SuperAdmin (by design), so it is seeded directly.
        const string email = "root@test.com", password = "Root@1234";
        await _host.WithDbAsync(async db =>
        {
            var salt = PasswordHelper.GenerateSalt();
            db.Users.Add(new User { Name = "Root", Email = email, Role = UserRole.SuperAdmin, Salt = salt, PasswordHash = PasswordHelper.HashPassword(password, salt) });
            await db.SaveChangesAsync();
        });

        var super = await _host.LoginAsync(email, password);
        (await super.PostAsJsonAsync("/api/users", new { name = "New Admin", email = $"adm_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Admin" }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var admin = await _host.AsAdmin();
        (await admin.PostAsJsonAsync("/api/users", new { name = "Sneaky", email = $"su_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "SuperAdmin" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // ...nor promote an existing user to SuperAdmin.
        (await admin.PutAsJsonAsync($"/api/users/{_host.Seed.TeacherId}", new { name = "Test Teacher", email = SeededData.TeacherEmail, role = "SuperAdmin" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact(Skip = "Pending: M7-I-05 — ExamProctors scoping for invigilators")]
    public async Task M7_I_05_InvigilatorSeesOnlyAssignedExams()
    {
        var (invigilatorId, email, password) = await _host.CreateUserAsync("Invigilator");
        var assignedExam = await _host.CreateExamAsync(new[] { _host.Seed.StudentId });

        var teacher = await _host.AsTeacher();
        (await teacher.PostAsJsonAsync($"/api/exams/{assignedExam}/proctors", new { userId = invigilatorId }))
            .StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

        var invigilator = await _host.LoginAsync(email, password);
        var resp = await invigilator.GetAsync("/api/exams");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var exams = await resp.Content.ReadFromJsonAsync<List<JsonElement>>();

        exams!.Select(e => e.GetProperty("examId").GetGuid()).Should().BeEquivalentTo(new[] { assignedExam });
        (await invigilator.GetAsync($"/api/exams/{_host.Seed.ExamId}")).StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
    }
}
