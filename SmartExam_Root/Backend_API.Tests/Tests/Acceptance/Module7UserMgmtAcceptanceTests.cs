using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M7 — User &amp; Role Management (TEST_CASES.md §M7, layer I).
/// M7-I-01 (bulk import, bad rows reported) → Module1And7Tests.ImportUsers_CreatesValid_SkipsDuplicates.
/// Departments/Courses CRUD, SuperAdmin-only admin management, ExamProctors (POST /api/exams/{id}/proctors)
/// scoping invigilators (Stage S3; M7-I-06…12 cover the edges of the same contracts).
/// </summary>
public class Module7UserMgmtAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    // ── Stage S3 additions: proctor assignment contract, invigilator scoping, privileged accounts ──

    private static Task<HttpResponseMessage> AssignAsync(HttpClient c, Guid examId, Guid userId) =>
        c.PostAsJsonAsync($"/api/exams/{examId}/proctors", new { userId });

    private Task<int> CountNotificationsAsync(Func<Backend_API.Models.Notifications.Notification, bool> match) =>
        _host.WithDbAsync(db => Task.FromResult(db.Notifications.AsEnumerable().Count(match)));

    [Fact]
    public async Task M7_I_06_ProctorAssignment_Contract_201ThenIdempotent200_ValidatedAndAudited()
    {
        var (invigilatorId, _, _) = await _host.CreateUserAsync("Invigilator");
        var teacher = await _host.AsTeacher();
        var examId = _host.Seed.ExamId;

        (await AssignAsync(teacher, examId, invigilatorId)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await AssignAsync(teacher, examId, invigilatorId)).StatusCode.Should().Be(HttpStatusCode.OK, "a repeat assignment is idempotent");

        var list = await (await teacher.GetAsync($"/api/exams/{examId}/proctors")).Content.ReadFromJsonAsync<List<JsonElement>>();
        list!.Should().ContainSingle().Which.GetProperty("userId").GetGuid().Should().Be(invigilatorId);
        list[0].GetProperty("role").GetString().Should().Be("Invigilator");

        // Only invigilators/teachers can proctor; the section's own teacher is already its supervisor.
        var bad = await AssignAsync(teacher, examId, _host.Seed.StudentId);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(bad)).Should().Be("INVALID_PROCTOR_ROLE");
        (await TestHost.ErrorCodeAsync(await AssignAsync(teacher, examId, _host.Seed.TeacherId))).Should().Be("ALREADY_OWNER");
        (await AssignAsync(teacher, examId, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AssignAsync(teacher, Guid.NewGuid(), invigilatorId)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Another section's teacher, the invigilator and a student cannot manage the exam's proctors.
        var (_, otherEmail, otherPassword) = await _host.CreateUserAsync("Teacher");
        var other = await _host.LoginAsync(otherEmail, otherPassword);
        (await AssignAsync(other, examId, invigilatorId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync($"/api/exams/{examId}/proctors")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.DeleteAsync($"/api/exams/{examId}/proctors/{invigilatorId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AssignAsync(await _host.AsStudent(), examId, invigilatorId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Admins manage any exam's proctors.
        var admin = await _host.AsAdmin();
        (await admin.DeleteAsync($"/api/exams/{examId}/proctors/{invigilatorId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.DeleteAsync($"/api/exams/{examId}/proctors/{invigilatorId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _host.AuditLogsAsync("PROCTOR_ASSIGNED")).Should().ContainSingle("the idempotent repeat is not a new assignment")
            .Which.GetProperty("entityId").GetString().Should().Be(examId.ToString());
        (await _host.AuditLogsAsync("PROCTOR_UNASSIGNED")).Should().ContainSingle();
    }

    [Fact]
    public async Task M7_I_07_Invigilator_AssignedExamOnly_NoAnswerKey_UnassignRevokesAccess()
    {
        var (invigilatorId, email, password) = await _host.CreateUserAsync("Invigilator");
        var invigilator = await _host.LoginAsync(email, password);
        // Own exam with a reference answer, an MCQ key and a hidden test case — none may reach a proctor.
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, questions: new[]
        {
            TestHost.TheoryQuestion(),
            TestHost.CodingQuestion(20, ("1 2", "3", false), ("5 5", "10", true))
        });

        (await (await invigilator.GetAsync("/api/exams")).Content.ReadFromJsonAsync<List<JsonElement>>())!.Should().BeEmpty();
        (await invigilator.GetAsync($"/api/exams/{examId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await invigilator.GetAsync($"/api/exams/{examId}/readiness")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var teacher = await _host.AsTeacher();
        await AssignAsync(teacher, examId, invigilatorId);

        var detail = await invigilator.GetAsync($"/api/exams/{examId}");
        detail.StatusCode.Should().Be(HttpStatusCode.OK);
        (await detail.Content.ReadAsStringAsync()).Should().NotContain("passwordHash");
        var questions = (await TestHost.JsonAsync(detail)).GetProperty("questions").EnumerateArray().ToList();
        questions.Should().NotBeEmpty();
        questions.Should().OnlyContain(q => q.GetProperty("referenceAnswer").ValueKind == JsonValueKind.Null
                                         && q.GetProperty("correctOptionIndex").ValueKind == JsonValueKind.Null,
            "proctors supervise, they never see the answer key");
        questions.SelectMany(q => q.GetProperty("testCases").EnumerateArray())
            .Should().ContainSingle("the hidden test case is withheld").Which.GetProperty("isHidden").GetBoolean().Should().BeFalse();
        (await (await invigilator.GetAsync("/api/exams")).Content.ReadFromJsonAsync<List<JsonElement>>())!
            .Select(e => e.GetProperty("examId").GetGuid()).Should().Equal(examId);
        (await invigilator.GetAsync($"/api/exams/{examId}/readiness")).StatusCode.Should().Be(HttpStatusCode.OK);

        // Invigilators supervise; they do not manage rosters, results or proctors.
        (await invigilator.GetAsync($"/api/exams/{examId}/results")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await invigilator.GetAsync($"/api/exams/{examId}/assignments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await AssignAsync(invigilator, examId, invigilatorId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await teacher.DeleteAsync($"/api/exams/{examId}/proctors/{invigilatorId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await invigilator.GetAsync("/api/exams")).Content.ReadFromJsonAsync<List<JsonElement>>())!.Should().BeEmpty();
        (await invigilator.GetAsync($"/api/exams/{examId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var hub = _host.CreateHub(TestHost.Principal(invigilatorId, "Invigilator"));
        await ((Func<Task>)(() => hub.Hub.JoinExamGroup(examId))).Should().ThrowAsync<HubException>();
    }

    [Fact]
    public async Task M7_I_08_HubGroup_OwnerAdminAndAssignedTeacherOnly()
    {
        var examId = _host.Seed.ExamId;
        var (otherTeacherId, _, _) = await _host.CreateUserAsync("Teacher");

        using (var hub = _host.CreateHub(TestHost.Principal(otherTeacherId, "Teacher")))
            await ((Func<Task>)(() => hub.Hub.JoinExamGroup(examId))).Should()
                .ThrowAsync<HubException>("a teacher may only watch exams of their own sections");

        using (var hub = _host.CreateHub(TestHost.Principal(_host.Seed.AdminId, "Admin")))
        {
            await hub.Hub.JoinExamGroup(examId);
            hub.Groups.Added.Should().ContainSingle();
            await ((Func<Task>)(() => hub.Hub.JoinExamGroup(Guid.NewGuid()))).Should()
                .ThrowAsync<HubException>("an unknown exam has no group");
        }

        // A teacher can co-proctor another section's exam once assigned.
        (await AssignAsync(await _host.AsTeacher(), examId, otherTeacherId)).StatusCode.Should().Be(HttpStatusCode.Created);
        using (var hub = _host.CreateHub(TestHost.Principal(otherTeacherId, "Teacher")))
        {
            await hub.Hub.JoinExamGroup(examId);
            hub.Groups.Added.Should().ContainSingle().Which.Group.Should().Be($"exam:{examId}");
        }
    }

    [Fact]
    public async Task M7_I_09_Invigilator_WarnsAndForceSubmits_OnlyInSupervisedExams()
    {
        var sid = await _host.StartSessionAsync(await _host.AsStudent(), _host.Seed.ExamId);
        var (invigilatorId, _, _) = await _host.CreateUserAsync("Invigilator");
        var principal = TestHost.Principal(invigilatorId, "Invigilator");
        var student = _host.Seed.StudentId.ToString();

        using (var hub = _host.CreateHub(principal))
        {
            await ((Func<Task>)(() => hub.Hub.SendWarning(student, "not yours"))).Should().ThrowAsync<HubException>();
            await ((Func<Task>)(() => hub.Hub.ForceSubmitSession(student))).Should().ThrowAsync<HubException>();
        }
        (await _host.WithDbAsync(async db => (await db.ExamSessions.FindAsync(sid))!.Status)).Should().Be(SessionStatus.InProgress);
        (await CountNotificationsAsync(n => n.RecipientId == _host.Seed.StudentId && n.Type == "Warning")).Should().Be(0);

        await AssignAsync(await _host.AsTeacher(), _host.Seed.ExamId, invigilatorId);
        using (var hub = _host.CreateHub(principal))
        {
            await hub.Hub.SendWarning(student, "Eyes on your screen");
            await hub.Hub.ForceSubmitSession(student);
        }
        (await _host.WithDbAsync(async db => (await db.ExamSessions.FindAsync(sid))!.Status)).Should().Be(SessionStatus.ForceSubmitted);
        (await CountNotificationsAsync(n => n.RecipientId == _host.Seed.StudentId && n.Body == "Eyes on your screen")).Should().Be(1);
    }

    private async Task<(HttpClient super, Guid superId)> SeedSuperAdminAsync()
    {
        const string email = "root2@test.com", password = "Root@1234";
        var id = Guid.NewGuid();
        await _host.WithDbAsync(async db =>
        {
            var salt = PasswordHelper.GenerateSalt();
            db.Users.Add(new User { UserId = id, Name = "Root", Email = email, Role = UserRole.SuperAdmin, Salt = salt, PasswordHash = PasswordHelper.HashPassword(password, salt) });
            await db.SaveChangesAsync();
        });
        return (await _host.LoginAsync(email, password), id);
    }

    [Fact]
    public async Task M7_I_10_AdminCannotManagePrivilegedAccounts_SuperAdminCan()
    {
        var (super, superId) = await SeedSuperAdminAsync();
        var created = await super.PostAsJsonAsync("/api/users", new { name = "Second Admin", email = $"adm2_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Admin" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var otherAdminId = (await TestHost.JsonAsync(created)).GetProperty("userId").GetGuid();

        var admin = await _host.AsAdmin();
        // Creating an Admin, and every action on another Admin or a SuperAdmin, needs a SuperAdmin.
        var create = await admin.PostAsJsonAsync("/api/users", new { name = "A", email = $"a_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Admin" });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await TestHost.ErrorCodeAsync(create)).Should().Be("SUPERADMIN_REQUIRED");
        (await admin.PutAsJsonAsync($"/api/users/{otherAdminId}", new { name = "Renamed", email = "x@test.com", role = "Admin" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/users/{otherAdminId}", new { name = "Demoted", email = "x@test.com", role = "Teacher" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PatchAsync($"/api/users/{otherAdminId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"/api/users/{otherAdminId}/force-logout", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/users/{superId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/users/{_host.Seed.TeacherId}", new { name = "Test Teacher", email = SeededData.TeacherEmail, role = "Admin" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.AuditLogsAsync("PRIVILEGED_ACCOUNT_DENIED")).Should().HaveCount(7);

        // Import: privileged rows are skipped and reported, ordinary rows still go through.
        var import = await admin.PostAsJsonAsync("/api/users/import", new
        {
            users = new object[]
            {
                new { name = "Imp Admin", email = $"ia_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Admin" },
                new { name = "Imp Teacher", email = $"it_{Guid.NewGuid():N}@test.com", password = "Pass@1234", role = "Teacher" }
            }
        });
        var importBody = await TestHost.JsonAsync(import);
        importBody.GetProperty("created").GetInt32().Should().Be(1);
        importBody.GetProperty("skipped")[0].GetProperty("reason").GetString().Should().Contain("SuperAdmin");

        // Admins still manage ordinary accounts and their own profile.
        (await admin.PutAsJsonAsync($"/api/users/{_host.Seed.AdminId}", new { name = "Admin Renamed", email = SeededData.AdminEmail, role = "Admin" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/users/{_host.Seed.AdminId}", new { name = "Admin Renamed", email = SeededData.AdminEmail, role = "SuperAdmin" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "nobody changes their own role");
        (await _host.CreateUserAsync("Invigilator")).Id.Should().NotBeEmpty();

        // A SuperAdmin promotes and manages admins.
        (await super.PutAsJsonAsync($"/api/users/{_host.Seed.TeacherId}", new { name = "Test Teacher", email = SeededData.TeacherEmail, role = "Admin" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await super.PatchAsync($"/api/users/{otherAdminId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task M7_I_11_Departments_ValidationAndRoles()
    {
        var admin = await _host.AsAdmin();
        (await admin.PostAsJsonAsync("/api/departments", new { name = "  " })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/departments", new { name = new string('x', 129) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var dup = await admin.PostAsJsonAsync("/api/departments", new { name = "computer science" });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict, "names are unique case-insensitively");
        (await TestHost.ErrorCodeAsync(dup)).Should().Be("DUPLICATE_DEPARTMENT");

        var created = await admin.PostAsJsonAsync("/api/departments", new { name = "  Mathematics " });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await TestHost.JsonAsync(created);
        var mathId = createdBody.GetProperty("deptId").GetGuid();
        createdBody.GetProperty("name").GetString().Should().Be("Mathematics");
        (await admin.PutAsJsonAsync($"/api/departments/{mathId}", new { name = "Computer Science" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/departments/{Guid.NewGuid()}", new { name = "Ghost" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/departments/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The seeded department lists its course (the Course → Department FK is DeptId).
        var cs = await TestHost.JsonAsync(await admin.GetAsync($"/api/departments/{_host.Seed.DeptId}"));
        cs.GetProperty("courseCount").GetInt32().Should().Be(1);
        cs.GetProperty("courses")[0].GetProperty("code").GetString().Should().Be("CS301");

        // Every staff role reads; students do not; only admins write.
        var (_, invEmail, invPassword) = await _host.CreateUserAsync("Invigilator");
        (await (await _host.LoginAsync(invEmail, invPassword)).GetAsync("/api/departments")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _host.AsStudent()).GetAsync("/api/departments")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await _host.AsTeacher()).DeleteAsync($"/api/departments/{mathId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await _host.AuditLogsAsync("DEPARTMENT_CREATED")).Should().ContainSingle()
            .Which.GetProperty("entityId").GetString().Should().Be(mathId.ToString());
    }

    [Fact]
    public async Task M7_I_12_Courses_ValidationNormalisationAndDeleteGuard()
    {
        var admin = await _host.AsAdmin();
        var deptId = _host.Seed.DeptId;

        var created = await admin.PostAsJsonAsync("/api/courses", new { deptId, name = "Compilers", code = " cs505 " });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await TestHost.JsonAsync(created);
        body.GetProperty("code").GetString().Should().Be("CS505", "codes are stored trimmed and upper-case");
        var id = body.GetProperty("courseId").GetGuid();

        (await admin.PostAsJsonAsync("/api/courses", new { deptId, name = "Dup", code = "cs301" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PutAsJsonAsync($"/api/courses/{id}", new { deptId, name = "Compilers", code = "CS301" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        var unknownDept = await admin.PostAsJsonAsync("/api/courses", new { deptId = Guid.NewGuid(), name = "X", code = "X1" });
        unknownDept.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await TestHost.ErrorCodeAsync(unknownDept)).Should().Be("UNKNOWN_DEPARTMENT");
        (await admin.PostAsJsonAsync("/api/courses", new { deptId, name = "", code = "X2" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/courses", new { deptId, name = "No code", code = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The same code is fine in another department.
        var other = (await TestHost.JsonAsync(await admin.PostAsJsonAsync("/api/departments", new { name = "Software Engineering" }))).GetProperty("deptId").GetGuid();
        (await admin.PostAsJsonAsync("/api/courses", new { deptId = other, name = "DS (SE)", code = "CS301" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await TestHost.JsonAsync(await admin.GetAsync($"/api/departments/{other}"))).GetProperty("courseCount").GetInt32().Should().Be(1);

        // A course with sections (the seeded CS301 owns the exam's section) cannot be deleted; nor a department with courses.
        var del = await admin.DeleteAsync($"/api/courses/{_host.Seed.CourseId}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await TestHost.ErrorCodeAsync(del)).Should().Be("COURSE_HAS_SECTIONS");
        (await admin.DeleteAsync($"/api/departments/{other}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var teacher = await _host.AsTeacher();
        (await teacher.GetAsync("/api/courses")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await teacher.PostAsJsonAsync("/api/courses", new { deptId, name = "Nope", code = "NO1" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await teacher.PutAsJsonAsync($"/api/courses/{id}", new { deptId, name = "Nope", code = "NO1" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await _host.AuditLogsAsync("COURSE_CREATED")).Should().HaveCount(2);
    }
}
