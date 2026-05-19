using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Only seed if the database is empty
        if (await db.Users.AnyAsync()) return;

        // --- Users ---
        var admin = new User
        {
            UserId = Guid.NewGuid(),
            Name = "Admin User",
            Email = "admin@smartexam.com",
            Role = UserRole.Admin,
            Salt = PasswordHelper.GenerateSalt()
        };
        admin.PasswordHash = PasswordHelper.HashPassword("Admin@123", admin.Salt);

        var teacher = new User
        {
            UserId = Guid.NewGuid(),
            Name = "Dr. Ahmed",
            Email = "teacher@smartexam.com",
            Role = UserRole.Teacher,
            Salt = PasswordHelper.GenerateSalt()
        };
        teacher.PasswordHash = PasswordHelper.HashPassword("Teacher@123", teacher.Salt);

        var student1 = new User
        {
            UserId = Guid.NewGuid(),
            Name = "Ali Hassan",
            Email = "ali@smartexam.com",
            Role = UserRole.Student,
            Salt = PasswordHelper.GenerateSalt()
        };
        student1.PasswordHash = PasswordHelper.HashPassword("Student@123", student1.Salt);

        var student2 = new User
        {
            UserId = Guid.NewGuid(),
            Name = "Sara Khan",
            Email = "sara@smartexam.com",
            Role = UserRole.Student,
            Salt = PasswordHelper.GenerateSalt()
        };
        student2.PasswordHash = PasswordHelper.HashPassword("Student@123", student2.Salt);

        await db.Users.AddRangeAsync(admin, teacher, student1, student2);

        // --- Lab & Workstations ---
        var lab = new Lab { Name = "Lab A", Location = "Block 3, Ground Floor" };
        var ws1 = new Workstation { Lab = lab, MachineNumber = "PC-01", IpAddress = "192.168.1.101" };
        var ws2 = new Workstation { Lab = lab, MachineNumber = "PC-02", IpAddress = "192.168.1.102" };
        await db.Labs.AddAsync(lab);
        await db.Workstations.AddRangeAsync(ws1, ws2);

        // --- Department, Course, Section ---
        var dept = new Department { Name = "Computer Science" };
        var course = new Course { Department = dept, Name = "Data Structures", Code = "CS301" };
        var section = new Section
        {
            Course = course,
            Teacher = teacher,
            Name = "BSCS-6A",
            Semester = "Fall 2025"
        };
        await db.Departments.AddAsync(dept);
        await db.Courses.AddAsync(course);
        await db.Sections.AddAsync(section);

        // --- Enroll students ---
        await db.SectionEnrollments.AddRangeAsync(
            new SectionEnrollment { Section = section, Student = student1 },
            new SectionEnrollment { Section = section, Student = student2 }
        );

        // --- Exam ---
        var exam = new Exam
        {
            Section = section,
            Title = "Mid-Term Lab Exam",
            StartTime = DateTime.UtcNow.AddMinutes(5),  // starts in 5 minutes so you can test
            DurationMinutes = 60,
            AllowedApps = "[\"code.exe\",\"codeblocks.exe\"]",
            AiEvaluationEnabled = true,
            PlagiarismThreshold = 70,
            Status = ExamStatus.Scheduled
        };

        var q1 = new Question
        {
            Exam = exam,
            Type = QuestionType.Coding,
            BodyText = "Write a C++ function that takes an array and returns the maximum element.",
            Marks = 20,
            OrderIndex = 1
        };
        var q1_tc1 = new TestCase { Question = q1, Input = "5\n3 1 4 1 5", ExpectedOutput = "5", IsHidden = false };
        var q1_tc2 = new TestCase { Question = q1, Input = "3\n-1 -5 -2", ExpectedOutput = "-1", IsHidden = true };

        var q2 = new Question
        {
            Exam = exam,
            Type = QuestionType.Theory,
            BodyText = "Explain the difference between a stack and a queue. Give one real-world example of each.",
            Marks = 10,
            OrderIndex = 2
        };

        await db.Exams.AddAsync(exam);
        await db.Questions.AddRangeAsync(q1, q2);
        await db.TestCases.AddRangeAsync(q1_tc1, q1_tc2);

        // --- Assign students to exam ---
        await db.ExamAssignments.AddRangeAsync(
            new ExamAssignment { Exam = exam, Student = student1, Workstation = ws1, IsEligible = true },
            new ExamAssignment { Exam = exam, Student = student2, Workstation = ws2, IsEligible = true }
        );

        await db.SaveChangesAsync();
        Console.WriteLine("✅ Database seeded successfully.");
    }
}
