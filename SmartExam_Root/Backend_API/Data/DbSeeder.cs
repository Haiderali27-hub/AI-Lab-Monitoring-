using Backend_API.Helpers;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Data;

public static class DbSeeder
{
    /// <summary>
    /// Seeds a MINIMAL demo dataset — and only when the database is empty.
    /// Restarting the backend no longer wipes real data; everything you create
    /// through the app persists. Demo records are labelled "(Demo)".
    /// </summary>
    public static async Task SeedAsync(AppDbContext db)
    {
        // Do not touch an already-populated database.
        if (await db.Users.AnyAsync())
        {
            Console.WriteLine("ℹ️ Database already has data — seeding skipped.");
            return;
        }

        // --- Users (the only accounts you need to log in) ---
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

        // NOTE: no pre-made device binding — students bind their REAL machine
        // on first login through the desktop client (Module 1).

        // --- Two labs ---
        // (1) A small demo lab with 2 sample seats (clearly labelled dummy data).
        var lab = new Lab { Name = "Lab A (Demo)", Location = "Block 3, Ground Floor" };
        var ws1 = new Workstation { Lab = lab, MachineNumber = "R1C1", IpAddress = "192.168.1.101" };
        var ws2 = new Workstation { Lab = lab, MachineNumber = "R1C2", IpAddress = "192.168.1.102" };
        await db.Labs.AddAsync(lab);
        await db.Workstations.AddRangeAsync(ws1, ws2);

        // (2) The REAL lab: any machine a student logs in from is auto-registered here,
        //     so its actual PC name + the student on it appears live. Starts empty.
        var engLab = new Lab { Name = "Systems Engineering Lab", Location = "Block A, Room 105" };
        await db.Labs.AddAsync(engLab);

        // --- Academic structure ---
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

        await db.SectionEnrollments.AddRangeAsync(
            new SectionEnrollment { Section = section, Student = student1 },
            new SectionEnrollment { Section = section, Student = student2 }
        );

        // --- Demo exam #1: window already open (started 2 min ago) so a student can start it right now ---
        var exam = new Exam
        {
            Section = section,
            Title = "Mid-Term Lab Exam (Demo)",
            Instructions = "Answer all questions. Only the whitelisted applications may be used. Ask the invigilator if your workstation misbehaves.",
            StartTime = DateTime.UtcNow.AddMinutes(-2),
            DurationMinutes = 90,
            AllowedApps = "[\"code.exe\",\"codeblocks.exe\",\"chrome.exe\"]",
            AiEvaluationEnabled = true,
            PlagiarismThreshold = 70,
            Status = ExamStatus.Scheduled
        };

        var q1 = new Question
        {
            Exam = exam,
            Type = QuestionType.Coding,
            BodyText = "Write a C++ function that takes an integer array and returns the maximum element. Read the array size on the first line, then the elements on the second line.",
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

        var q3 = new Question
        {
            Exam = exam,
            Type = QuestionType.Mcq,
            BodyText = "Which data structure follows the Last-In-First-Out (LIFO) principle?",
            Marks = 5,
            OrderIndex = 3,
            OptionsJson = System.Text.Json.JsonSerializer.Serialize(new[] { "Queue", "Stack", "Linked List", "Binary Tree" }),
            CorrectOptionIndex = 1
        };

        await db.Exams.AddAsync(exam);
        await db.Questions.AddRangeAsync(q1, q2, q3);
        await db.TestCases.AddRangeAsync(q1_tc1, q1_tc2);

        // Assign both students; seats are auto-mapped from the Systems Engineering Lab on login.
        await db.ExamAssignments.AddRangeAsync(
            new ExamAssignment { Exam = exam, Student = student1, IsEligible = true },
            new ExamAssignment { Exam = exam, Student = student2, IsEligible = true }
        );

        // --- Demo exam #2: ended with one graded submission, so Results/Analytics pages have data ---
        var examEnded = new Exam
        {
            Section = section,
            Title = "Final Lab Exam (Demo)",
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
        Console.WriteLine("✅ Empty database seeded with minimal demo data.");
    }
}
