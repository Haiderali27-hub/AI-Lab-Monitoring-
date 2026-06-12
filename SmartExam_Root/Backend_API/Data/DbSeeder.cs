using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        // Always reset database to clean state for E2E tests
        db.AuditLogs.RemoveRange(db.AuditLogs);
        db.Notifications.RemoveRange(db.Notifications);
        db.ExamReports.RemoveRange(db.ExamReports);
        db.PlagiarismResults.RemoveRange(db.PlagiarismResults);
        db.TeacherGradeOverrides.RemoveRange(db.TeacherGradeOverrides);
        db.AiGradingResults.RemoveRange(db.AiGradingResults);
        db.Answers.RemoveRange(db.Answers);
        db.MonitoringEvents.RemoveRange(db.MonitoringEvents);
        db.ExamSessions.RemoveRange(db.ExamSessions);
        db.ExamAssignments.RemoveRange(db.ExamAssignments);
        db.TestCases.RemoveRange(db.TestCases);
        db.Questions.RemoveRange(db.Questions);
        db.Exams.RemoveRange(db.Exams);
        db.SectionEnrollments.RemoveRange(db.SectionEnrollments);
        db.Sections.RemoveRange(db.Sections);
        db.Courses.RemoveRange(db.Courses);
        db.Departments.RemoveRange(db.Departments);
        db.Workstations.RemoveRange(db.Workstations);
        db.Labs.RemoveRange(db.Labs);
        db.UserSessions.RemoveRange(db.UserSessions);
        db.DeviceBindings.RemoveRange(db.DeviceBindings);
        db.Users.RemoveRange(db.Users);
        await db.SaveChangesAsync();

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

        // --- Device Bindings ---
        var binding1 = new DeviceBinding
        {
            UserId = student1.UserId,
            HwidHash = "TEST-HWID-HASH-ALI-12345",
            RegisteredAt = DateTime.UtcNow.AddDays(-5),
            LastSeenAt = DateTime.UtcNow.AddMinutes(-10)
        };
        await db.DeviceBindings.AddAsync(binding1);

        // --- Ended Exam for E2E Results page testing ---
        var examEnded = new Exam
        {
            Section = section,
            Title = "Final Lab Exam",
            StartTime = DateTime.UtcNow.AddHours(-3),
            DurationMinutes = 120,
            AllowedApps = "[\"code.exe\",\"codeblocks.exe\"]",
            AiEvaluationEnabled = true,
            PlagiarismThreshold = 70,
            Status = ExamStatus.Ended
        };
        await db.Exams.AddAsync(examEnded);

        var qEnded = new Question
        {
            Exam = examEnded,
            Type = QuestionType.Coding,
            BodyText = "Write a function to reverse a string.",
            Marks = 20,
            OrderIndex = 1
        };
        await db.Questions.AddAsync(qEnded);

        var sessionEnded = new ExamSession
        {
            Exam = examEnded,
            Student = student1,
            StartedAt = DateTime.UtcNow.AddHours(-3),
            SubmittedAt = DateTime.UtcNow.AddHours(-2),
            Status = SessionStatus.Submitted
        };
        await db.ExamSessions.AddAsync(sessionEnded);

        var answerEnded = new Answer
        {
            ExamSession = sessionEnded,
            Question = qEnded,
            AnswerText = "void reverse(string &s) { reverse(s.begin(), s.end()); }",
            SubmittedAt = DateTime.UtcNow.AddHours(-2),
            LastSavedAt = DateTime.UtcNow.AddHours(-2.5)
        };
        await db.Answers.AddAsync(answerEnded);

        await db.SaveChangesAsync();
        Console.WriteLine("✅ Database seeded successfully.");
    }
}
