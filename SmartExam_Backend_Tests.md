# SmartExam — Backend Test Guide
### xUnit Automated Tests for All 6 Modules
### Runs in terminal · Tests every API endpoint · Pass/Fail per test

---

## Table of Contents

1. [What Kind of Testing This Is](#1-what-kind-of-testing-this-is)
2. [Setup — Create the Test Project](#2-setup--create-the-test-project)
3. [Test Database Setup](#3-test-database-setup)
4. [Seed Data for Tests](#4-seed-data-for-tests)
5. [Test Factory — Shared Setup](#5-test-factory--shared-setup)
6. [Module 1 & 2 — Auth & User Tests](#6-module-1--2--auth--user-tests)
7. [Module 2 — Exam Tests](#7-module-2--exam-tests)
8. [Module 3 — AI & Monitoring Tests](#8-module-3--ai--monitoring-tests)
9. [Module 4 — Analytics Tests](#9-module-4--analytics-tests)
10. [Module 5 — Student Portal Tests](#10-module-5--student-portal-tests)
11. [Module 6 — Notifications Tests](#11-module-6--notifications-tests)
12. [How to Run Tests](#12-how-to-run-tests)
13. [How to Read the Logs](#13-how-to-read-the-logs)
14. [How to Fix Failing Tests](#14-how-to-fix-failing-tests)
15. [How to Rerun Tests](#15-how-to-rerun-tests)

---

## 1. What Kind of Testing This Is

**Type: Black-Box Integration Testing**

This is exactly what you described — the tests treat the API like a black box. They:
- Send a real HTTP request to an endpoint
- Check the response status code
- Check the response body contains the right data
- Do not care about the internal code — only the output

Every test is independent. Each test:
1. Sets up its own clean data
2. Makes a real API call
3. Checks the result
4. Cleans up after itself

**What gets tested:**
- Every endpoint across all 6 modules (47 tests total)
- Authentication and role-based access
- Device binding logic
- Exam session flow
- Analytics calculations
- Notification sending
- Student portal data isolation

**What does NOT get tested here:**
- The AI microservice (Python) — that has its own tests
- The desktop app — that is the frontend test guide
- Email delivery — mocked in tests

---

## 2. Setup — Create the Test Project

Run these commands from inside your `SmartExam/` root folder (same level as `Backend_API/`):

```bash
# Create the test project
dotnet new xunit -n Backend_API.Tests
cd Backend_API.Tests

# Add reference to your main project
dotnet add reference ../Backend_API/Backend_API.csproj

# Install required packages
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 9.0.0
dotnet add package Microsoft.EntityFrameworkCore.InMemory --version 9.0.0
dotnet add package FluentAssertions --version 6.12.0
dotnet add package Bogus --version 35.5.1

# Go back to root and add test project to solution (if you have a .sln file)
cd ..
dotnet sln add Backend_API.Tests/Backend_API.Tests.csproj

# Confirm it builds
cd Backend_API.Tests
dotnet build
# Should say: Build succeeded.
```

### Create the folder structure

```bash
mkdir Helpers Tests/Auth Tests/Users Tests/Exams Tests/Analytics Tests/Student Tests/Notifications
```

Final structure:
```
Backend_API.Tests/
├── Helpers/
│   ├── TestWebAppFactory.cs     ← Spins up the API in memory for testing
│   ├── TestSeeder.cs            ← Creates test data
│   └── AuthHelper.cs            ← Gets JWT tokens for tests
├── Tests/
│   ├── Auth/
│   │   └── AuthTests.cs
│   ├── Users/
│   │   └── UserTests.cs
│   ├── Exams/
│   │   └── ExamTests.cs
│   ├── Analytics/
│   │   └── AnalyticsTests.cs
│   ├── Student/
│   │   └── StudentPortalTests.cs
│   └── Notifications/
│       └── NotificationTests.cs
└── Backend_API.Tests.csproj
```

---

## 3. Test Database Setup

Tests use an **in-memory database** — a fake database that lives only during the test run. This means:
- Tests never touch your real Neon database
- Every test run starts with a clean slate
- Tests run fast (no network calls to Neon)

---

## 4. Seed Data for Tests

This is the data that gets created before every test. Copy this file exactly.

### `Helpers/TestSeeder.cs`

```csharp
using Backend_API.Data;
using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;

namespace Backend_API.Tests.Helpers;

// Holds IDs of seeded entities so tests can reference them
public class SeededData
{
    public Guid AdminId { get; set; }
    public Guid TeacherId { get; set; }
    public Guid StudentId { get; set; }
    public Guid Student2Id { get; set; }
    public Guid LabId { get; set; }
    public Guid WorkstationId { get; set; }
    public Guid DeptId { get; set; }
    public Guid CourseId { get; set; }
    public Guid SectionId { get; set; }
    public Guid ExamId { get; set; }
    public Guid Question1Id { get; set; }
    public Guid Question2Id { get; set; }
    public Guid Assignment1Id { get; set; }

    // Credentials for each role (used by AuthHelper)
    public const string AdminEmail    = "admin@test.com";
    public const string AdminPassword = "Admin@123";
    public const string TeacherEmail    = "teacher@test.com";
    public const string TeacherPassword = "Teacher@123";
    public const string StudentEmail    = "student@test.com";
    public const string StudentPassword = "Student@123";
    public const string Student2Email    = "student2@test.com";
    public const string Student2Password = "Student@123";
    public const string TestHwid = "TEST_HWID_HASH_ABC123";
    public const string WrongHwid = "WRONG_HWID_HASH_XYZ999";
}

public static class TestSeeder
{
    public static async Task<SeededData> SeedAsync(AppDbContext db)
    {
        var data = new SeededData();

        // ── Users ────────────────────────────────────────────────────────────

        var adminSalt = PasswordHelper.GenerateSalt();
        var admin = new User
        {
            UserId = data.AdminId = Guid.NewGuid(),
            Name = "Test Admin",
            Email = SeededData.AdminEmail,
            Role = UserRole.Admin,
            Salt = adminSalt,
            PasswordHash = PasswordHelper.HashPassword(SeededData.AdminPassword, adminSalt),
            IsActive = true
        };

        var teacherSalt = PasswordHelper.GenerateSalt();
        var teacher = new User
        {
            UserId = data.TeacherId = Guid.NewGuid(),
            Name = "Test Teacher",
            Email = SeededData.TeacherEmail,
            Role = UserRole.Teacher,
            Salt = teacherSalt,
            PasswordHash = PasswordHelper.HashPassword(SeededData.TeacherPassword, teacherSalt),
            IsActive = true
        };

        var studentSalt = PasswordHelper.GenerateSalt();
        var student = new User
        {
            UserId = data.StudentId = Guid.NewGuid(),
            Name = "Test Student",
            Email = SeededData.StudentEmail,
            Role = UserRole.Student,
            Salt = studentSalt,
            PasswordHash = PasswordHelper.HashPassword(SeededData.StudentPassword, studentSalt),
            IsActive = true
        };

        var student2Salt = PasswordHelper.GenerateSalt();
        var student2 = new User
        {
            UserId = data.Student2Id = Guid.NewGuid(),
            Name = "Test Student Two",
            Email = SeededData.Student2Email,
            Role = UserRole.Student,
            Salt = student2Salt,
            PasswordHash = PasswordHelper.HashPassword(SeededData.Student2Password, student2Salt),
            IsActive = true
        };

        await db.Users.AddRangeAsync(admin, teacher, student, student2);

        // ── Device binding for student (pre-registered) ───────────────────────

        db.DeviceBindings.Add(new DeviceBinding
        {
            UserId = data.StudentId,
            HwidHash = SeededData.TestHwid
        });

        // ── Lab & Workstation ────────────────────────────────────────────────

        var lab = new Lab { LabId = data.LabId = Guid.NewGuid(), Name = "Test Lab A", Location = "Block 1" };
        var ws = new Workstation { WorkstationId = data.WorkstationId = Guid.NewGuid(), LabId = lab.LabId, MachineNumber = "PC-01", IpAddress = "192.168.1.1" };
        await db.Labs.AddAsync(lab);
        await db.Workstations.AddAsync(ws);

        // ── Academic structure ───────────────────────────────────────────────

        var dept = new Department { DeptId = data.DeptId = Guid.NewGuid(), Name = "Computer Science" };
        var course = new Course { CourseId = data.CourseId = Guid.NewGuid(), DeptId = dept.DeptId, Name = "Data Structures", Code = "CS301" };
        var section = new Section { SectionId = data.SectionId = Guid.NewGuid(), CourseId = course.CourseId, TeacherId = teacher.UserId, Name = "BSCS-6A", Semester = "Fall 2025" };
        await db.Departments.AddAsync(dept);
        await db.Courses.AddAsync(course);
        await db.Sections.AddAsync(section);

        await db.SectionEnrollments.AddRangeAsync(
            new SectionEnrollment { SectionId = section.SectionId, UserId = student.UserId },
            new SectionEnrollment { SectionId = section.SectionId, UserId = student2.UserId }
        );

        // ── Exam (starts 1 minute ago so students can start it) ──────────────

        var exam = new Exam
        {
            ExamId = data.ExamId = Guid.NewGuid(),
            SectionId = section.SectionId,
            Title = "Test Exam",
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            DurationMinutes = 60,
            AllowedApps = "[\"code.exe\"]",
            AiEvaluationEnabled = true,
            PlagiarismThreshold = 70,
            Status = ExamStatus.Scheduled
        };

        var q1 = new Question
        {
            QuestionId = data.Question1Id = Guid.NewGuid(),
            ExamId = exam.ExamId,
            Type = QuestionType.Coding,
            BodyText = "Write a function to reverse a string.",
            Marks = 20,
            OrderIndex = 1
        };

        var q2 = new Question
        {
            QuestionId = data.Question2Id = Guid.NewGuid(),
            ExamId = exam.ExamId,
            Type = QuestionType.Theory,
            BodyText = "Explain the difference between a stack and a queue.",
            Marks = 10,
            OrderIndex = 2
        };

        var assignment = new ExamAssignment
        {
            AssignmentId = data.Assignment1Id = Guid.NewGuid(),
            ExamId = exam.ExamId,
            UserId = student.UserId,
            WorkstationId = ws.WorkstationId,
            IsEligible = true
        };

        var assignment2 = new ExamAssignment
        {
            ExamId = exam.ExamId,
            UserId = student2.UserId,
            WorkstationId = ws.WorkstationId,
            IsEligible = true
        };

        await db.Exams.AddAsync(exam);
        await db.Questions.AddRangeAsync(q1, q2);
        await db.ExamAssignments.AddRangeAsync(assignment, assignment2);

        await db.SaveChangesAsync();
        return data;
    }
}
```

---

## 5. Test Factory — Shared Setup

### `Helpers/TestWebAppFactory.cs`

```csharp
using Backend_API.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend_API.Tests.Helpers;

public class TestWebAppFactory : WebApplicationFactory<Program>
{
    public SeededData SeededData { get; private set; } = null!;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the real PostgreSQL DbContext
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            // Replace with in-memory database
            var dbName = $"TestDb_{Guid.NewGuid()}"; // unique per factory instance
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(dbName));

            // Seed test data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            SeededData = TestSeeder.SeedAsync(db).GetAwaiter().GetResult();
        });

        // Use test JWT secret matching appsettings
        builder.UseSetting("Jwt:Secret", "SmartExam_SuperSecretKey_ChangeThis_AtLeast32Chars!!");
        builder.UseSetting("Jwt:Issuer", "SmartExam");
        builder.UseSetting("Jwt:Audience", "SmartExamUsers");
        builder.UseSetting("Jwt:ExpiryMinutes", "480");

        // Disable real email in tests
        builder.UseSetting("Email:SenderEmail", "test@test.com");
        builder.UseSetting("Email:AppPassword", "fake_password");
    }
}
```

### `Helpers/AuthHelper.cs`

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace Backend_API.Tests.Helpers;

public static class AuthHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static async Task<string> GetTokenAsync(HttpClient client, string email, string password, string? hwid = null)
    {
        var payload = hwid != null
            ? new { email, password, hwidHash = hwid }
            : (object)new { email, password };

        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("token").GetString()!;
    }

    public static void SetToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task AuthorizeAs(HttpClient client, string email, string password, string? hwid = null)
    {
        var token = await GetTokenAsync(client, email, password, hwid);
        SetToken(client, token);
    }
}
```

---

## 6. Module 1 & 2 — Auth & User Tests

### `Tests/Auth/AuthTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Auth;

public class AuthTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public AuthTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    // ── TEST 1 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_Admin_WithValidCredentials_Returns200WithToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededData.AdminEmail,
            password = SeededData.AdminPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        string? token = body?.GetProperty("token").GetString();
        token.Should().NotBeNullOrEmpty();
    }

    // ── TEST 2 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededData.AdminEmail,
            password = "WrongPassword123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── TEST 3 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_WithNonExistentEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "nobody@nowhere.com",
            password = "anything"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── TEST 4 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_Student_WithCorrectHwid_Returns200AndDeviceBoundTrue()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededData.StudentEmail,
            password = SeededData.StudentPassword,
            hwidHash = SeededData.TestHwid
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        bool deviceBound = body!.GetProperty("deviceBound").GetBoolean();
        deviceBound.Should().BeTrue();
    }

    // ── TEST 5 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_Student_WithWrongHwid_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededData.StudentEmail,
            password = SeededData.StudentPassword,
            hwidHash = SeededData.WrongHwid
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── TEST 6 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Login_Student_WithoutHwid_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededData.StudentEmail,
            password = SeededData.StudentPassword
            // no hwidHash
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── TEST 7 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetMe_WithValidToken_Returns200WithUserInfo()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        string? email = body?.GetProperty("email").GetString();
        email.Should().Be(SeededData.AdminEmail);
    }

    // ── TEST 8 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── TEST 9 ────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Logout_WithValidToken_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

### `Tests/Users/UserTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Users;

public class UserTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public UserTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    // ── TEST 10 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetAllUsers_AsAdmin_Returns200WithUserList()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<dynamic>>();
        body.Should().HaveCountGreaterThanOrEqualTo(4); // admin + teacher + 2 students
    }

    // ── TEST 11 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetAllUsers_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TEST 12 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetUsers_FilterByStudentRole_ReturnsOnlyStudents()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/users?role=Student");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<dynamic>>();
        body.Should().HaveCountGreaterThanOrEqualTo(2);
        foreach (var user in body!)
            user.GetProperty("role").GetString().Should().Be("Student");
    }

    // ── TEST 13 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateUser_AsAdmin_Returns201WithNewUser()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.PostAsJsonAsync("/api/users", new
        {
            name = "New Test Student",
            email = $"newstudent_{Guid.NewGuid()}@test.com",
            password = "NewPass@123",
            role = "Student"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("role").GetString().Should().Be("Student");
    }

    // ── TEST 14 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateUser_WithDuplicateEmail_Returns409()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        await _client.PostAsJsonAsync("/api/users", new
        {
            name = "Duplicate", email = "duplicate@test.com", password = "Pass@123", role = "Student"
        });
        // Second call with same email
        var response = await _client.PostAsJsonAsync("/api/users", new
        {
            name = "Duplicate Again", email = "duplicate@test.com", password = "Pass@123", role = "Student"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ── TEST 15 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task CreateUser_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PostAsJsonAsync("/api/users", new
        {
            name = "Hacker", email = "hacker@test.com", password = "Hack@123", role = "Admin"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TEST 16 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task ResetDeviceBinding_AsAdmin_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.DeleteAsync($"/api/users/{_seed.StudentId}/device-binding");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TEST 17 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task ForceLogout_AsAdmin_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.PostAsync($"/api/users/{_seed.TeacherId}/force-logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

---

## 7. Module 2 — Exam Tests

### `Tests/Exams/ExamTests.cs`

```csharp
using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Exams;

public class ExamTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public ExamTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    // ── TEST 18 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetExams_AsTeacher_Returns200WithExamList()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync("/api/exams");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<dynamic>>();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── TEST 19 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetExams_AsStudent_ReturnsOnlyAssignedExams()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/exams");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<dynamic>>();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
        // Student should only see their assigned exams, not all exams
        foreach (var exam in body!)
            exam.GetProperty("isEligible"); // student view has isEligible field
    }

    // ── TEST 20 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetExamById_AsTeacher_Returns200WithQuestions()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync($"/api/exams/{_seed.ExamId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        var questions = body?.GetProperty("questions");
        questions.Should().NotBeNull();
    }

    // ── TEST 21 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task StartExamSession_AsEligibleStudent_Returns200WithSessionId()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        string? sessionId = body?.GetProperty("sessionId").GetString();
        sessionId.Should().NotBeNullOrEmpty();
    }

    // ── TEST 22 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task SaveAnswer_DuringActiveSession_Returns200()
    {
        // First start a session
        await AuthHelper.AuthorizeAs(_client, SeededData.Student2Email, SeededData.Student2Password, SeededData.TestHwid + "_2");
        // Note: student2 has no device binding yet — it will register on first login

        // Re-seed: for this test use student1 who is already bound
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<dynamic>();
        var sessionId = sessionBody!.GetProperty("sessionId").GetString();

        // Now save an answer
        var saveResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/save-answer",
            new { questionId = _seed.Question1Id, answerText = "string reverse(string s) { return s; }" }
        );

        saveResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TEST 23 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecordMonitoringEvent_AsStudent_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<dynamic>();
        var sessionId = sessionBody!.GetProperty("sessionId").GetString();

        var eventResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/monitoring-event",
            new
            {
                eventType = "Heartbeat",
                payload = "{\"activeWindow\":\"code.exe\",\"timestamp\":\"2025-01-01T10:00:00Z\"}"
            }
        );

        eventResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TEST 24 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task SubmitExam_AsStudent_Returns200WithSubmittedAt()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<dynamic>();
        var sessionId = sessionBody!.GetProperty("sessionId").GetString();

        var submitResp = await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitBody = await submitResp.Content.ReadFromJsonAsync<dynamic>();
        submitBody?.GetProperty("submittedAt").GetString().Should().NotBeNullOrEmpty();
    }

    // ── TEST 25 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task SubmitExam_Twice_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionBody = await sessionResp.Content.ReadFromJsonAsync<dynamic>();
        var sessionId = sessionBody!.GetProperty("sessionId").GetString();

        await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);
        var secondSubmit = await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);

        secondSubmit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
```

---

## 8. Module 3 — AI & Monitoring Tests

```csharp
// Tests/Exams/MonitoringTests.cs
using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Exams;

public class MonitoringTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public MonitoringTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    // ── TEST 26 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecordViolationEvent_IsStoredWithViolationType()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<dynamic>())!
            .GetProperty("sessionId").GetString();

        var violationResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/monitoring-event",
            new
            {
                eventType = "Violation",
                payload = "{\"blockedApp\":\"chrome.exe\",\"reason\":\"Unauthorized application\"}"
            }
        );

        violationResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TEST 27 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecordHeartbeat_AsStudent_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<dynamic>())!
            .GetProperty("sessionId").GetString();

        var hbResp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{sessionId}/monitoring-event",
            new { eventType = "Heartbeat", payload = "{\"activeWindow\":\"SmartExam.exe\"}" }
        );

        hbResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── TEST 28 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task RecordMonitoringEvent_WithoutSession_Returns404Or400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var fakeSessionId = Guid.NewGuid();

        var resp = await _client.PostAsJsonAsync(
            $"/api/exams/sessions/{fakeSessionId}/monitoring-event",
            new { eventType = "Heartbeat", payload = "{}" }
        );

        resp.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}
```

---

## 9. Module 4 — Analytics Tests

```csharp
// Tests/Analytics/AnalyticsTests.cs
using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Analytics;

public class AnalyticsTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public AnalyticsTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    // ── TEST 29 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetSystemAnalytics_AsAdmin_Returns200WithAllFields()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/analytics/system");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("totalStudents").GetInt32().Should().BeGreaterThanOrEqualTo(2);
        body?.GetProperty("totalTeachers").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        body?.GetProperty("examsByMonth").Should().NotBeNull();
    }

    // ── TEST 30 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetSystemAnalytics_AsTeacher_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync("/api/analytics/system");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TEST 31 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetExamSummary_AsTeacher_Returns200WithScoreDistribution()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync($"/api/analytics/exams/{_seed.ExamId}/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("scoreDistribution").Should().NotBeNull();
        body?.GetProperty("questionStats").Should().NotBeNull();
    }

    // ── TEST 32 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetExamSummary_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync($"/api/analytics/exams/{_seed.ExamId}/summary");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TEST 33 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GeneratePdfReport_AsTeacher_Returns200WithDownloadUrl()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsync($"/api/analytics/exams/{_seed.ExamId}/report", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        string? url = body?.GetProperty("downloadUrl").GetString();
        url.Should().Contain(".pdf");
    }

    // ── TEST 34 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetExamSummary_WithInvalidExamId_Returns404()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.GetAsync($"/api/analytics/exams/{Guid.NewGuid()}/summary");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
```

---

## 10. Module 5 — Student Portal Tests

```csharp
// Tests/Student/StudentPortalTests.cs
using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Student;

public class StudentPortalTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public StudentPortalTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    // ── TEST 35 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetStudentDashboard_AsStudent_Returns200WithRequiredFields()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("totalExamsTaken").Should().NotBeNull();
        body?.GetProperty("averageScore").Should().NotBeNull();
        body?.GetProperty("recentExams").Should().NotBeNull();
        body?.GetProperty("performanceTrend").Should().NotBeNull();
    }

    // ── TEST 36 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetStudentDashboard_AsAdmin_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/student/dashboard");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TEST 37 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetMyExams_AsStudent_ReturnsOnlyAssignedExams()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/exams");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<dynamic>>();
        body.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    // ── TEST 38 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetMyViolations_AsStudent_Returns200WithViolationList()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/violations");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("totalViolations").Should().NotBeNull();
        body?.GetProperty("violations").Should().NotBeNull();
    }

    // ── TEST 39 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetPerformanceTrend_AsStudent_ReturnsArrayForChart()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/performance-trend");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<dynamic>>();
        body.Should().NotBeNull();
    }

    // ── TEST 40 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetExamResult_BeforeExamEnds_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        // Exam is still Scheduled/Active — not Ended
        var response = await _client.GetAsync($"/api/student/exams/{_seed.ExamId}/result");
        // Either 404 (no session) or 400 (exam not ended)
        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.BadRequest);
    }
}
```

---

## 11. Module 6 — Notifications Tests

```csharp
// Tests/Notifications/NotificationTests.cs
using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Notifications;

public class NotificationTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public NotificationTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    // ── TEST 41 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetNotifications_AsAdmin_Returns200WithUnreadCount()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("unreadCount").Should().NotBeNull();
        body?.GetProperty("notifications").Should().NotBeNull();
    }

    // ── TEST 42 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task NotifyExamScheduled_AsTeacher_Returns200AndStudentsReceiveNotification()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var notifyResp = await _client.PostAsync(
            $"/api/notifications/exam/{_seed.ExamId}/notify-scheduled", null);

        notifyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now check student received it
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var notifResp = await _client.GetAsync("/api/notifications");
        var body = await notifResp.Content.ReadFromJsonAsync<dynamic>();
        int unread = body!.GetProperty("unreadCount").GetInt32();
        unread.Should().BeGreaterThanOrEqualTo(1);
    }

    // ── TEST 43 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task MarkAllNotificationsRead_Returns200WithMarkedCount()
    {
        // First send a notification so there's something to mark
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        await _client.PostAsync($"/api/notifications/exam/{_seed.ExamId}/notify-scheduled", null);

        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PatchAsync("/api/notifications/read-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("markedRead").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    // ── TEST 44 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task SendAnnouncement_AsTeacher_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsJsonAsync(
            $"/api/notifications/exam/{_seed.ExamId}/announce",
            new { title = "Test Announcement", message = "This is a test message.", sendEmail = false }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("message").GetString().Should().Contain("students");
    }

    // ── TEST 45 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task SendAnnouncement_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PostAsJsonAsync(
            $"/api/notifications/exam/{_seed.ExamId}/announce",
            new { title = "Hack", message = "hacked", sendEmail = false }
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── TEST 46 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task GetStudentNotifications_AsStudent_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<dynamic>();
        body?.GetProperty("unreadCount").Should().NotBeNull();
    }

    // ── TEST 47 ───────────────────────────────────────────────────────────────
    [Fact]
    public async Task NotifyGradesReleased_AsTeacher_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsync(
            $"/api/notifications/exam/{_seed.ExamId}/notify-grades", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

---

## 12. How to Run Tests

### Run ALL tests at once

```bash
# From inside Backend_API.Tests/ folder
cd SmartExam/Backend_API.Tests
dotnet test
```

### Run tests for one module only

```bash
# Auth tests only
dotnet test --filter "FullyQualifiedName~AuthTests"

# User tests only
dotnet test --filter "FullyQualifiedName~UserTests"

# Exam tests only
dotnet test --filter "FullyQualifiedName~ExamTests"

# Analytics tests only
dotnet test --filter "FullyQualifiedName~AnalyticsTests"

# Student portal tests only
dotnet test --filter "FullyQualifiedName~StudentPortalTests"

# Notification tests only
dotnet test --filter "FullyQualifiedName~NotificationTests"
```

### Run a single test by name

```bash
dotnet test --filter "FullyQualifiedName~Login_Admin_WithValidCredentials_Returns200WithToken"
```

### Run with detailed output (see each test name as it runs)

```bash
dotnet test --logger "console;verbosity=detailed"
```

### Run and save results to a file

```bash
dotnet test --logger "trx;LogFileName=TestResults.trx" --results-directory ./TestResults
```

---

## 13. How to Read the Logs

### What a passing run looks like

```
Test run for Backend_API.Tests.dll (.NETCoreApp,Version=v9.0)
Microsoft (R) Test Execution Command Line Tool Version 17.x

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed: 0, Passed: 47, Skipped: 0, Total: 47, Duration: 3.2s
```

### What a failing test looks like

```
Failed   Login_Student_WithWrongHwid_Returns401 [84ms]
  Error Message:
   Expected HTTP status code to be 401 Unauthorized,
   but found 200 OK.
  Stack Trace:
     at AuthTests.Login_Student_WithWrongHwid_Returns401()
```

**Reading a failure:**
- **Test name** — tells you exactly which test failed
- **Error Message** — what was expected vs what actually happened
- **Stack Trace** — which line in the test file failed

### Verbose mode — see every test as it runs

```bash
dotnet test --logger "console;verbosity=detailed"
```

Output:
```
[xUnit.net 00:00:00.12]   Starting: Backend_API.Tests
  Passed Backend_API.Tests.Tests.Auth.AuthTests.Login_Admin_WithValidCredentials_Returns200WithToken [42ms]
  Passed Backend_API.Tests.Tests.Auth.AuthTests.Login_WithWrongPassword_Returns401 [8ms]
  Failed Backend_API.Tests.Tests.Auth.AuthTests.Login_Student_WithWrongHwid_Returns401 [84ms]
```

---

## 14. How to Fix Failing Tests

| Failure message | What it means | How to fix |
|---|---|---|
| `Expected 401 but got 200` | Your endpoint is not checking HWID correctly | Check `AuthController.cs` — the HWID comparison logic |
| `Expected 403 but got 200` | Role-based auth is missing on that endpoint | Add `[Authorize(Roles = "Admin")]` to the controller method |
| `Expected 200 but got 500` | Your endpoint is crashing | Run the backend normally and hit the endpoint in Swagger to see the actual error |
| `Expected 201 but got 400` | Validation error — request body is wrong | Check the DTO model — required fields may be missing |
| `Expected count >= 1 but got 0` | Seed data not created correctly | Check `TestSeeder.cs` — the relationship may be missing a foreign key |
| `Object reference not set` | JSON property doesn't exist in response | The endpoint is returning a different shape than expected — check the controller return value |
| `Connection refused` | In-memory DB not set up | Check `TestWebAppFactory.cs` — `UseInMemoryDatabase` must replace the real Npgsql provider |
| `Build failed` before tests run | Compile error | Run `dotnet build` first and fix any compile errors shown |

### Step-by-step debugging a failing test

```bash
# Step 1 — Run only the failing test with verbose output
dotnet test --filter "FullyQualifiedName~YourFailingTestName" --logger "console;verbosity=detailed"

# Step 2 — Add a temporary Console.WriteLine in the test to see the response body
var body = await response.Content.ReadAsStringAsync();
Console.WriteLine("RESPONSE BODY: " + body);

# Step 3 — Check the actual status code
Console.WriteLine("STATUS: " + response.StatusCode);

# Step 4 — Fix the endpoint, rebuild, rerun
dotnet build
dotnet test --filter "FullyQualifiedName~YourFailingTestName"
```

---

## 15. How to Rerun Tests

### Rerun all tests from scratch

```bash
dotnet test
```

### Rerun only failed tests

```bash
# Run once and save results
dotnet test --logger "trx;LogFileName=results.trx" --results-directory ./TestResults

# Rerun only failed tests from last run
dotnet test --logger "trx;LogFileName=results.trx" --results-directory ./TestResults -- RunConfiguration.TreatNoTestsAsError=true
```

### Clean and rerun (if tests are caching old results)

```bash
dotnet clean
dotnet build
dotnet test
```

### Watch mode — reruns automatically when you save a file

```bash
dotnet watch test
# Now every time you save any .cs file, tests rerun automatically
```

### Full test run with coverage report

```bash
dotnet add package coverlet.collector
dotnet test --collect:"XPlat Code Coverage" --results-directory ./TestResults
```

---

## Test Summary — All 47 Tests

| # | Test Name | Module | Expected |
|---|-----------|--------|----------|
| 1 | Login_Admin_ValidCredentials | Auth | 200 + token |
| 2 | Login_WrongPassword | Auth | 401 |
| 3 | Login_NonExistentEmail | Auth | 401 |
| 4 | Login_Student_CorrectHwid | Auth | 200 + deviceBound=true |
| 5 | Login_Student_WrongHwid | Auth | 401 |
| 6 | Login_Student_NoHwid | Auth | 400 |
| 7 | GetMe_ValidToken | Auth | 200 + email |
| 8 | GetMe_NoToken | Auth | 401 |
| 9 | Logout_ValidToken | Auth | 200 |
| 10 | GetAllUsers_AsAdmin | Users | 200 + list |
| 11 | GetAllUsers_AsStudent | Users | 403 |
| 12 | GetUsers_FilterByStudent | Users | 200 + students only |
| 13 | CreateUser_AsAdmin | Users | 201 |
| 14 | CreateUser_DuplicateEmail | Users | 409 |
| 15 | CreateUser_AsStudent | Users | 403 |
| 16 | ResetDeviceBinding_AsAdmin | Users | 200 |
| 17 | ForceLogout_AsAdmin | Users | 200 |
| 18 | GetExams_AsTeacher | Exams | 200 + list |
| 19 | GetExams_AsStudent | Exams | 200 + assigned only |
| 20 | GetExamById_AsTeacher | Exams | 200 + questions |
| 21 | StartSession_EligibleStudent | Exams | 200 + sessionId |
| 22 | SaveAnswer_ActiveSession | Exams | 200 |
| 23 | RecordMonitoringEvent | Exams | 200 |
| 24 | SubmitExam | Exams | 200 + submittedAt |
| 25 | SubmitExam_Twice | Exams | 400 |
| 26 | RecordViolationEvent | Monitoring | 200 |
| 27 | RecordHeartbeat | Monitoring | 200 |
| 28 | RecordEvent_NoSession | Monitoring | 404/400 |
| 29 | GetSystemAnalytics_AsAdmin | Analytics | 200 + fields |
| 30 | GetSystemAnalytics_AsTeacher | Analytics | 403 |
| 31 | GetExamSummary_AsTeacher | Analytics | 200 + distribution |
| 32 | GetExamSummary_AsStudent | Analytics | 403 |
| 33 | GeneratePdfReport | Analytics | 200 + URL |
| 34 | GetExamSummary_InvalidId | Analytics | 404 |
| 35 | GetStudentDashboard_AsStudent | Student | 200 + fields |
| 36 | GetStudentDashboard_AsAdmin | Student | 403 |
| 37 | GetMyExams_AsStudent | Student | 200 + list |
| 38 | GetMyViolations_AsStudent | Student | 200 |
| 39 | GetPerformanceTrend_AsStudent | Student | 200 + array |
| 40 | GetExamResult_BeforeEnd | Student | 400/404 |
| 41 | GetNotifications_AsAdmin | Notifications | 200 |
| 42 | NotifyExamScheduled_StudentReceives | Notifications | 200 + unread++ |
| 43 | MarkAllRead | Notifications | 200 |
| 44 | SendAnnouncement_AsTeacher | Notifications | 200 |
| 45 | SendAnnouncement_AsStudent | Notifications | 403 |
| 46 | GetStudentNotifications | Notifications | 200 |
| 47 | NotifyGradesReleased | Notifications | 200 |
