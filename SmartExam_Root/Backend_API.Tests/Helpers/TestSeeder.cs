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
    public const string TestHwid2 = "TEST_HWID_HASH_ABC123_2";
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
