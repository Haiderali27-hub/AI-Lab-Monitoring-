# SmartExam — Modules 4, 5 & 6 Backend Guide
### Reporting & Analytics · Student Portal · Notifications
### Built on top of existing Backend_API project

---

## Table of Contents

1. [Overview — What You Are Adding](#1-overview--what-you-are-adding)
2. [New NuGet Packages](#2-new-nuget-packages)
3. [Configuration Updates](#3-configuration-updates)
4. [New Database Tables](#4-new-database-tables)
5. [Module 4 — Reporting & Analytics](#5-module-4--reporting--analytics)
6. [Module 5 — Student Performance Portal](#6-module-5--student-performance-portal)
7. [Module 6 — Notification & Communication System](#7-module-6--notification--communication-system)
8. [Wire Everything into Program.cs](#8-wire-everything-into-programcs)
9. [Run Migrations](#9-run-migrations)
10. [Postman Tests](#10-postman-tests)

---

## 1. Overview — What You Are Adding

Everything in this guide goes into your **existing** `Backend_API` project. You are not creating a new project. You are adding new folders, models, controllers, and services on top of what you already built.

Here is exactly what gets added:

| Module | What It Does | New Endpoints |
|--------|-------------|---------------|
| **M4 — Analytics** | Aggregates exam data into charts and exportable PDF reports | 6 endpoints |
| **M5 — Student Portal** | Students view their own grades, AI feedback, violation history, and performance trends via a web browser | 7 endpoints |
| **M6 — Notifications** | Sends email and in-app notifications for exam events, grade releases, violations | 5 endpoints |

### Where to add files

All new files go inside the existing `Backend_API/` folder. New folders to create:

```bash
# Run from inside Backend_API/
mkdir -p Controllers/Analytics Controllers/Student Controllers/Notifications
mkdir -p Services/Analytics Services/Notifications
mkdir -p Models/Analytics Models/Notifications
mkdir -p DTOs/Analytics DTOs/Student DTOs/Notifications
```

---

## 2. New NuGet Packages

Run these from inside `Backend_API/`:

```bash
# PDF generation (for report exports)
dotnet add package QuestPDF --version 2024.3.4

# Email sending via Gmail SMTP
dotnet add package MailKit --version 4.7.1

# For nicer date handling in analytics queries
dotnet add package NodaTime --version 3.1.11
```

Verify they installed:
```bash
dotnet restore
dotnet build
# Should say: Build succeeded.
```

---

## 3. Configuration Updates

Add these new sections to your `appsettings.json`. Open it and add the blocks below inside the root `{}` object, after your existing `Jwt` section.

```json
{
  "ConnectionStrings": { ... },
  "Jwt": { ... },

  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": 587,
    "SenderEmail": "YOUR_GMAIL_ADDRESS@gmail.com",
    "SenderName": "SmartExam System",
    "AppPassword": "YOUR_GMAIL_APP_PASSWORD"
  },

  "Analytics": {
    "ReportOutputPath": "Reports/"
  }
}
```

### How to get a Gmail App Password (free, takes 2 minutes)

1. Go to your Google account → **Security**
2. Enable **2-Step Verification** if not already on
3. Go to **App Passwords** (search for it in Google account settings)
4. Create a new App Password — select **Mail** and **Windows Computer**
5. Google gives you a 16-character password like `abcd efgh ijkl mnop`
6. Remove the spaces and paste it as `AppPassword` in your config
7. Put your Gmail address as `SenderEmail`

> Your real Gmail password is never used. The App Password is separate and can be revoked anytime.

---

## 4. New Database Tables

Add these model files and then register them in `AppDbContext`.

---

### `Models/Analytics/ExamReport.cs`

```csharp
namespace Backend_API.Models.Analytics;

// Stores generated PDF report metadata
// The actual PDF file is saved to disk
public class ExamReport
{
    public Guid ReportId { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid GeneratedBy { get; set; }       // Teacher or Admin who requested it
    public string ReportType { get; set; } = string.Empty; // "ExamSummary" | "StudentDetail" | "PlagiarismReport"
    public string FilePath { get; set; } = string.Empty;   // Path on server disk
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User GeneratedByUser { get; set; } = null!;
}
```

---

### `Models/Notifications/Notification.cs`

```csharp
namespace Backend_API.Models.Notifications;

// Stores every notification sent (in-app + email)
public class Notification
{
    public Guid NotificationId { get; set; } = Guid.NewGuid();
    public Guid RecipientId { get; set; }           // Who receives it
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "ExamScheduled" | "GradeReleased" | "ViolationWarning" | "EligibilityChanged" | "ExamReminder"
    public bool IsRead { get; set; } = false;
    public bool EmailSent { get; set; } = false;
    public string? RelatedEntityId { get; set; }     // examId or sessionId this notification is about
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User Recipient { get; set; } = null!;
}
```

---

### Update `Data/AppDbContext.cs`

Open your existing `AppDbContext.cs` and add these lines:

```csharp
// Add these using statements at the top
using Backend_API.Models.Analytics;
using Backend_API.Models.Notifications;

// Add these DbSets inside the class (after your existing DbSets)
public DbSet<ExamReport> ExamReports => Set<ExamReport>();
public DbSet<Notification> Notifications => Set<Notification>();
```

Also add these relationship configurations inside `OnModelCreating`:

```csharp
// ExamReport: two FK to User (no cascade)
modelBuilder.Entity<ExamReport>()
    .HasOne(r => r.GeneratedByUser)
    .WithMany()
    .HasForeignKey(r => r.GeneratedBy)
    .OnDelete(DeleteBehavior.Restrict);

// Notification: FK to recipient
modelBuilder.Entity<Notification>()
    .HasOne(n => n.Recipient)
    .WithMany()
    .HasForeignKey(n => n.RecipientId)
    .OnDelete(DeleteBehavior.Cascade);

// Index for fast unread notification queries
modelBuilder.Entity<Notification>()
    .HasIndex(n => new { n.RecipientId, n.IsRead });
```

---

## 5. Module 4 — Reporting & Analytics

### What this module does

- Gives teachers a breakdown of every exam: score distribution, question difficulty, violation counts
- Gives admins a system-wide view: exam trends over time, most active labs, student performance across courses
- Lets anyone generate and download a PDF report of any exam

---

### `Services/Analytics/AnalyticsService.cs`

```csharp
using Backend_API.Data;
using Backend_API.Models.Analytics;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Backend_API.Services.Analytics;

public class AnalyticsService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public AnalyticsService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
        // Set QuestPDF license to community (free)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ── Exam Summary Analytics ────────────────────────────────────────────────

    public async Task<ExamSummaryAnalytics> GetExamSummaryAsync(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Questions)
            .Include(e => e.Sessions).ThenInclude(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(e => e.Sessions).ThenInclude(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .Include(e => e.Sessions).ThenInclude(s => s.MonitoringEvents)
            .FirstOrDefaultAsync(e => e.ExamId == examId)
            ?? throw new KeyNotFoundException("Exam not found.");

        int totalMarks = exam.Questions.Sum(q => q.Marks);
        var submittedSessions = exam.Sessions
            .Where(s => s.Status != Models.Enums.SessionStatus.InProgress)
            .ToList();

        // Calculate score for each student
        var scores = submittedSessions.Select(session =>
        {
            double earned = session.Answers.Sum(a =>
                a.TeacherGradeOverride?.FinalMarks
                ?? a.AiGradingResult?.SuggestedMarks
                ?? 0);
            return totalMarks > 0 ? (earned / totalMarks) * 100 : 0;
        }).ToList();

        // Score distribution buckets: 0-20, 21-40, 41-60, 61-80, 81-100
        var distribution = new Dictionary<string, int>
        {
            ["0-20"]   = scores.Count(s => s <= 20),
            ["21-40"]  = scores.Count(s => s > 20 && s <= 40),
            ["41-60"]  = scores.Count(s => s > 40 && s <= 60),
            ["61-80"]  = scores.Count(s => s > 60 && s <= 80),
            ["81-100"] = scores.Count(s => s > 80),
        };

        // Per-question average score
        var questionStats = exam.Questions.Select(q =>
        {
            var questionAnswers = submittedSessions
                .SelectMany(s => s.Answers)
                .Where(a => a.QuestionId == q.QuestionId)
                .ToList();

            double avgMarks = questionAnswers.Any()
                ? questionAnswers.Average(a =>
                    a.TeacherGradeOverride?.FinalMarks
                    ?? a.AiGradingResult?.SuggestedMarks
                    ?? 0)
                : 0;

            return new QuestionStat
            {
                QuestionId = q.QuestionId,
                ShortLabel = $"Q{q.OrderIndex}",
                FullText = q.BodyText.Length > 60 ? q.BodyText[..60] + "..." : q.BodyText,
                TotalMarks = q.Marks,
                AverageMarksEarned = Math.Round(avgMarks, 1),
                DifficultyPercent = q.Marks > 0 ? Math.Round((avgMarks / q.Marks) * 100, 1) : 0
            };
        }).ToList();

        // Violation stats
        var violations = submittedSessions
            .SelectMany(s => s.MonitoringEvents)
            .Where(e => e.EventType == Models.Enums.MonitoringEventType.Violation)
            .ToList();

        return new ExamSummaryAnalytics
        {
            ExamId = examId,
            ExamTitle = exam.Title,
            TotalStudents = submittedSessions.Count,
            TotalMarks = totalMarks,
            AverageScore = scores.Any() ? Math.Round(scores.Average(), 1) : 0,
            HighestScore = scores.Any() ? Math.Round(scores.Max(), 1) : 0,
            LowestScore = scores.Any() ? Math.Round(scores.Min(), 1) : 0,
            PassRate = scores.Any() ? Math.Round(scores.Count(s => s >= 50) * 100.0 / scores.Count, 1) : 0,
            ScoreDistribution = distribution,
            QuestionStats = questionStats,
            TotalViolations = violations.Count,
            StudentsWithViolations = submittedSessions.Count(s =>
                s.MonitoringEvents.Any(e => e.EventType == Models.Enums.MonitoringEventType.Violation))
        };
    }

    // ── Admin System Analytics ────────────────────────────────────────────────

    public async Task<SystemAnalytics> GetSystemAnalyticsAsync()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);

        // Exams per month (last 6 months)
        var examsByMonth = await _db.Exams
            .Where(e => e.StartTime >= now.AddMonths(-6))
            .GroupBy(e => new { e.StartTime.Year, e.StartTime.Month })
            .Select(g => new MonthCount
            {
                Label = $"{g.Key.Month}/{g.Key.Year}",
                Count = g.Count()
            })
            .ToListAsync();

        // Top courses by exam count
        var topCourses = await _db.Exams
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .GroupBy(e => e.Section.Course.Name)
            .Select(g => new { CourseName = g.Key, ExamCount = g.Count() })
            .OrderByDescending(x => x.ExamCount)
            .Take(5)
            .ToListAsync();

        // Violation trend (last 30 days by day)
        var violationTrend = await _db.MonitoringEvents
            .Where(e => e.EventType == Models.Enums.MonitoringEventType.Violation
                     && e.RecordedAt >= thirtyDaysAgo)
            .GroupBy(e => e.RecordedAt.Date)
            .Select(g => new DayCount { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return new SystemAnalytics
        {
            TotalStudents = await _db.Users.CountAsync(u => u.Role == Models.Enums.UserRole.Student),
            TotalTeachers = await _db.Users.CountAsync(u => u.Role == Models.Enums.UserRole.Teacher),
            TotalExams = await _db.Exams.CountAsync(),
            TotalExamsLast30Days = await _db.Exams.CountAsync(e => e.StartTime >= thirtyDaysAgo),
            TotalViolationsLast30Days = await _db.MonitoringEvents.CountAsync(
                e => e.EventType == Models.Enums.MonitoringEventType.Violation && e.RecordedAt >= thirtyDaysAgo),
            ExamsByMonth = examsByMonth,
            TopCoursesByExamCount = topCourses.Select(x => new MonthCount { Label = x.CourseName, Count = x.ExamCount }).ToList(),
            ViolationTrendLast30Days = violationTrend
        };
    }

    // ── PDF Report Generation ─────────────────────────────────────────────────

    public async Task<string> GenerateExamReportPdfAsync(Guid examId, Guid requestedByUserId)
    {
        var analytics = await GetExamSummaryAsync(examId);
        var exam = await _db.Exams
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .FirstAsync(e => e.ExamId == examId);

        // Ensure reports directory exists
        var outputDir = _config["Analytics:ReportOutputPath"] ?? "Reports/";
        Directory.CreateDirectory(outputDir);

        var fileName = $"Exam_Report_{examId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
        var filePath = Path.Combine(outputDir, fileName);

        // Build PDF with QuestPDF
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().Text("SmartExam — Exam Report")
                        .FontSize(20).Bold().FontColor(Colors.Blue.Medium);
                    col.Item().Text(analytics.ExamTitle)
                        .FontSize(14).Bold();
                    col.Item().Text($"{exam.Section.Course.Name} • Generated {DateTime.UtcNow:dd MMM yyyy HH:mm}")
                        .FontColor(Colors.Grey.Medium);
                    col.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Blue.Medium);
                });

                page.Content().Column(col =>
                {
                    // Summary stats
                    col.Item().PaddingVertical(8).Text("Summary Statistics").Bold().FontSize(13);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                        void AddCell(string label, string value, bool header = false)
                        {
                            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(c =>
                            {
                                c.Item().Text(label).FontColor(Colors.Grey.Medium).FontSize(9);
                                c.Item().Text(value).Bold().FontSize(header ? 12 : 11);
                            });
                        }
                        AddCell("Total Students", analytics.TotalStudents.ToString());
                        AddCell("Average Score", $"{analytics.AverageScore}%");
                        AddCell("Pass Rate (≥50%)", $"{analytics.PassRate}%");
                        AddCell("Total Violations", analytics.TotalViolations.ToString());
                        AddCell("Highest Score", $"{analytics.HighestScore}%");
                        AddCell("Lowest Score", $"{analytics.LowestScore}%");
                        AddCell("Total Marks", analytics.TotalMarks.ToString());
                        AddCell("Students w/ Violations", analytics.StudentsWithViolations.ToString());
                    });

                    // Question difficulty
                    col.Item().PaddingTop(16).Text("Per-Question Performance").Bold().FontSize(13);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(40); c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn();
                        });
                        // Headers
                        foreach (var h in new[] { "#", "Question", "Total Marks", "Avg Earned", "Difficulty %" })
                            table.Cell().Background(Colors.Blue.Medium).Padding(6)
                                .Text(h).Bold().FontColor(Colors.White);
                        // Rows
                        foreach (var q in analytics.QuestionStats)
                        {
                            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(q.ShortLabel);
                            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(q.FullText);
                            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(q.TotalMarks.ToString());
                            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text(q.AverageMarksEarned.ToString());
                            table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Text($"{q.DifficultyPercent}%");
                        }
                    });

                    // Score distribution
                    col.Item().PaddingTop(16).Text("Score Distribution").Bold().FontSize(13);
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { foreach (var _ in analytics.ScoreDistribution) c.RelativeColumn(); });
                        foreach (var bucket in analytics.ScoreDistribution)
                            table.Cell().Background(Colors.Blue.Lighten3).Border(1)
                                .BorderColor(Colors.Blue.Lighten2).Padding(8).Column(c =>
                                {
                                    c.Item().Text(bucket.Key).FontSize(9).FontColor(Colors.Grey.Medium);
                                    c.Item().Text(bucket.Value.ToString()).Bold().FontSize(14);
                                    c.Item().Text("students").FontSize(8).FontColor(Colors.Grey.Medium);
                                });
                    });
                });

                page.Footer().AlignCenter()
                    .Text(x =>
                    {
                        x.Span("SmartExam — Confidential Report • Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
            });
        }).GeneratePdf(filePath);

        // Save report metadata to database
        _db.ExamReports.Add(new ExamReport
        {
            ExamId = examId,
            GeneratedBy = requestedByUserId,
            ReportType = "ExamSummary",
            FilePath = filePath
        });
        await _db.SaveChangesAsync();

        return filePath;
    }
}

// ── Analytics DTOs ────────────────────────────────────────────────────────────

public class ExamSummaryAnalytics
{
    public Guid ExamId { get; set; }
    public string ExamTitle { get; set; } = string.Empty;
    public int TotalStudents { get; set; }
    public int TotalMarks { get; set; }
    public double AverageScore { get; set; }
    public double HighestScore { get; set; }
    public double LowestScore { get; set; }
    public double PassRate { get; set; }
    public Dictionary<string, int> ScoreDistribution { get; set; } = new();
    public List<QuestionStat> QuestionStats { get; set; } = new();
    public int TotalViolations { get; set; }
    public int StudentsWithViolations { get; set; }
}

public class QuestionStat
{
    public Guid QuestionId { get; set; }
    public string ShortLabel { get; set; } = string.Empty;
    public string FullText { get; set; } = string.Empty;
    public int TotalMarks { get; set; }
    public double AverageMarksEarned { get; set; }
    public double DifficultyPercent { get; set; }
}

public class SystemAnalytics
{
    public int TotalStudents { get; set; }
    public int TotalTeachers { get; set; }
    public int TotalExams { get; set; }
    public int TotalExamsLast30Days { get; set; }
    public int TotalViolationsLast30Days { get; set; }
    public List<MonthCount> ExamsByMonth { get; set; } = new();
    public List<MonthCount> TopCoursesByExamCount { get; set; } = new();
    public List<DayCount> ViolationTrendLast30Days { get; set; } = new();
}

public class MonthCount { public string Label { get; set; } = string.Empty; public int Count { get; set; } }
public class DayCount { public DateTime Date { get; set; } public int Count { get; set; } }
```

---

### `Controllers/Analytics/AnalyticsController.cs`

```csharp
using Backend_API.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Backend_API.Controllers.Analytics;

[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analytics;
    public AnalyticsController(AnalyticsService analytics) => _analytics = analytics;

    // GET /api/analytics/exams/{examId}/summary
    // Used by: Teacher Results page charts, Admin exam detail
    [HttpGet("exams/{examId:guid}/summary")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GetExamSummary(Guid examId)
    {
        try
        {
            var result = await _analytics.GetExamSummaryAsync(examId);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // GET /api/analytics/system
    // Used by: Admin dashboard charts
    [HttpGet("system")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetSystemAnalytics()
    {
        var result = await _analytics.GetSystemAnalyticsAsync();
        return Ok(result);
    }

    // POST /api/analytics/exams/{examId}/report
    // Generates a PDF and returns the download URL
    [HttpPost("exams/{examId:guid}/report")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GenerateReport(Guid examId)
    {
        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var filePath = await _analytics.GenerateExamReportPdfAsync(examId, userId);
        var fileName = Path.GetFileName(filePath);
        return Ok(new { message = "Report generated.", downloadUrl = $"/api/analytics/reports/{fileName}" });
    }

    // GET /api/analytics/reports/{fileName}
    // Downloads the generated PDF file
    [HttpGet("reports/{fileName}")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public IActionResult DownloadReport(string fileName)
    {
        var outputDir = "Reports/";
        var filePath = Path.Combine(outputDir, fileName);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "Report not found." });

        var bytes = System.IO.File.ReadAllBytes(filePath);
        return File(bytes, "application/pdf", fileName);
    }

    // GET /api/analytics/exams/{examId}/reports
    // Lists previously generated reports for an exam
    [HttpGet("exams/{examId:guid}/reports")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GetReportHistory(Guid examId)
    {
        var reports = await _analytics._db.ExamReports // expose via internal for now
            .Where(r => r.ExamId == examId)
            .Select(r => new
            {
                r.ReportId,
                r.ReportType,
                r.GeneratedAt,
                FileName = Path.GetFileName(r.FilePath)
            })
            .OrderByDescending(r => r.GeneratedAt)
            .ToListAsync();
        return Ok(reports);
    }
}
```

> **Fix the direct `_db` access:** Add a public property `public AppDbContext Db => _db;` in `AnalyticsService` and change `_analytics._db` to `_analytics.Db` in the controller, or inject `AppDbContext` directly into the controller for that one query. Either works.

---

## 6. Module 5 — Student Performance Portal

### What this module does

A web-based portal (separate from the admin panel — students open it in a browser on their own device) where students log in and see:
- All their past exams and scores
- Per-question AI feedback on their answers
- Their violation history with details
- Performance trend across multiple exams over time

Students already exist in your database. This module adds new **read-only** endpoints that return a student's own data. Students cannot see other students' data — the endpoints enforce this at the authorization level.

---

### `Controllers/Student/StudentPortalController.cs`

```csharp
using Backend_API.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Backend_API.Controllers.Student;

[ApiController]
[Route("api/student")]
[Authorize(Roles = "Student")]
public class StudentPortalController : ControllerBase
{
    private readonly AppDbContext _db;
    public StudentPortalController(AppDbContext db) => _db = db;

    // Helper — gets the logged-in student's ID from JWT
    private Guid Me => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // GET /api/student/dashboard
    // Returns summary stats + recent exams for the student's personal dashboard
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var studentId = Me;

        var sessions = await _db.ExamSessions
            .Where(s => s.UserId == studentId && s.Status != Models.Enums.SessionStatus.InProgress)
            .Include(s => s.Exam).ThenInclude(e => e.Section).ThenInclude(sec => sec.Course)
            .Include(s => s.Exam).ThenInclude(e => e.Questions)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .Include(s => s.MonitoringEvents)
            .OrderByDescending(s => s.StartedAt)
            .ToListAsync();

        var examResults = sessions.Select(s =>
        {
            int total = s.Exam.Questions.Sum(q => q.Marks);
            double earned = s.Answers.Sum(a =>
                a.TeacherGradeOverride?.FinalMarks ?? a.AiGradingResult?.SuggestedMarks ?? 0);
            double pct = total > 0 ? Math.Round((earned / total) * 100, 1) : 0;
            int violations = s.MonitoringEvents.Count(e => e.EventType == Models.Enums.MonitoringEventType.Violation);

            return new
            {
                s.SessionId,
                s.Exam.ExamId,
                s.Exam.Title,
                CourseName = s.Exam.Section.Course.Name,
                s.StartedAt,
                s.SubmittedAt,
                s.Status,
                TotalMarks = total,
                EarnedMarks = Math.Round(earned, 1),
                ScorePercent = pct,
                ViolationCount = violations,
                Passed = pct >= 50
            };
        }).ToList();

        // Performance trend — score percent per exam over time
        var trend = examResults
            .OrderBy(r => r.StartedAt)
            .Select(r => new { r.Title, r.ScorePercent, r.StartedAt })
            .ToList();

        return Ok(new
        {
            TotalExamsTaken = examResults.Count,
            AverageScore = examResults.Any() ? Math.Round(examResults.Average(r => r.ScorePercent), 1) : 0,
            ExamsPassed = examResults.Count(r => r.Passed),
            TotalViolations = examResults.Sum(r => r.ViolationCount),
            RecentExams = examResults.Take(5),
            PerformanceTrend = trend
        });
    }

    // GET /api/student/exams
    // All exams the student has taken or is assigned to
    [HttpGet("exams")]
    public async Task<IActionResult> GetMyExams()
    {
        var studentId = Me;

        var assignments = await _db.ExamAssignments
            .Where(a => a.UserId == studentId)
            .Include(a => a.Exam).ThenInclude(e => e.Section).ThenInclude(s => s.Course)
            .Include(a => a.Exam).ThenInclude(e => e.Questions)
            .Select(a => new
            {
                a.Exam.ExamId,
                a.Exam.Title,
                CourseName = a.Exam.Section.Course.Name,
                SectionName = a.Exam.Section.Name,
                a.Exam.StartTime,
                a.Exam.DurationMinutes,
                a.Exam.Status,
                a.IsEligible,
                QuestionCount = a.Exam.Questions.Count,
                TotalMarks = a.Exam.Questions.Sum(q => q.Marks)
            })
            .OrderByDescending(a => a.StartTime)
            .ToListAsync();

        return Ok(assignments);
    }

    // GET /api/student/exams/{examId}/result
    // Full result for one exam — answers, AI feedback, marks
    [HttpGet("exams/{examId:guid}/result")]
    public async Task<IActionResult> GetMyExamResult(Guid examId)
    {
        var studentId = Me;

        var session = await _db.ExamSessions
            .Where(s => s.ExamId == examId && s.UserId == studentId)
            .Include(s => s.Exam).ThenInclude(e => e.Questions)
            .Include(s => s.Answers).ThenInclude(a => a.Question)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .FirstOrDefaultAsync();

        if (session is null)
            return NotFound(new { message = "No result found for this exam." });

        // Students can only see results after exam ends
        if (session.Exam.Status != Models.Enums.ExamStatus.Ended)
            return BadRequest(new { message = "Results are not available until the exam ends." });

        var answers = session.Answers.Select(a =>
        {
            double marks = a.TeacherGradeOverride?.FinalMarks ?? a.AiGradingResult?.SuggestedMarks ?? 0;
            return new
            {
                a.AnswerId,
                QuestionText = a.Question.BodyText,
                QuestionType = a.Question.Type.ToString(),
                TotalMarks = a.Question.Marks,
                a.AnswerText,
                EarnedMarks = marks,
                AiFeedback = a.AiGradingResult == null ? null : new
                {
                    a.AiGradingResult.SuggestedMarks,
                    a.AiGradingResult.Justification,
                    a.AiGradingResult.Confidence
                },
                TeacherOverridden = a.TeacherGradeOverride != null
            };
        }).ToList();

        int totalMarks = session.Exam.Questions.Sum(q => q.Marks);
        double totalEarned = answers.Sum(a => a.EarnedMarks);

        return Ok(new
        {
            session.SessionId,
            session.Exam.Title,
            session.StartedAt,
            session.SubmittedAt,
            session.Status,
            TotalMarks = totalMarks,
            EarnedMarks = Math.Round(totalEarned, 1),
            ScorePercent = totalMarks > 0 ? Math.Round((totalEarned / totalMarks) * 100, 1) : 0,
            Passed = totalMarks > 0 && (totalEarned / totalMarks) >= 0.5,
            Answers = answers
        });
    }

    // GET /api/student/violations
    // Student's own violation history across all exams
    [HttpGet("violations")]
    public async Task<IActionResult> GetMyViolations()
    {
        var studentId = Me;

        var violations = await _db.MonitoringEvents
            .Where(e => e.ExamSession.UserId == studentId
                     && e.EventType == Models.Enums.MonitoringEventType.Violation)
            .Include(e => e.ExamSession).ThenInclude(s => s.Exam)
            .OrderByDescending(e => e.RecordedAt)
            .Select(e => new
            {
                e.EventId,
                ExamTitle = e.ExamSession.Exam.Title,
                e.RecordedAt,
                e.Payload
            })
            .ToListAsync();

        return Ok(new
        {
            TotalViolations = violations.Count,
            Violations = violations
        });
    }

    // GET /api/student/performance-trend
    // Score percent for each completed exam, ordered by date — used for line chart
    [HttpGet("performance-trend")]
    public async Task<IActionResult> GetPerformanceTrend()
    {
        var studentId = Me;

        var sessions = await _db.ExamSessions
            .Where(s => s.UserId == studentId && s.Status != Models.Enums.SessionStatus.InProgress)
            .Include(s => s.Exam).ThenInclude(e => e.Questions)
            .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
            .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
            .OrderBy(s => s.StartedAt)
            .ToListAsync();

        var trend = sessions.Select(s =>
        {
            int total = s.Exam.Questions.Sum(q => q.Marks);
            double earned = s.Answers.Sum(a =>
                a.TeacherGradeOverride?.FinalMarks ?? a.AiGradingResult?.SuggestedMarks ?? 0);
            return new
            {
                ExamTitle = s.Exam.Title,
                Date = s.StartedAt.ToString("MMM dd"),
                ScorePercent = total > 0 ? Math.Round((earned / total) * 100, 1) : 0
            };
        });

        return Ok(trend);
    }

    // GET /api/student/notifications
    // Student's own notifications (exam reminders, grade releases etc.)
    [HttpGet("notifications")]
    public async Task<IActionResult> GetMyNotifications()
    {
        var studentId = Me;

        var notifications = await _db.Notifications
            .Where(n => n.RecipientId == studentId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.NotificationId,
                n.Title,
                n.Body,
                n.Type,
                n.IsRead,
                n.CreatedAt,
                n.RelatedEntityId
            })
            .ToListAsync();

        return Ok(new
        {
            UnreadCount = notifications.Count(n => !n.IsRead),
            Notifications = notifications
        });
    }

    // PATCH /api/student/notifications/{id}/read
    // Mark a notification as read
    [HttpPatch("notifications/{notificationId:guid}/read")]
    public async Task<IActionResult> MarkNotificationRead(Guid notificationId)
    {
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.NotificationId == notificationId && n.RecipientId == Me);

        if (notification is null) return NotFound();

        notification.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok(new { message = "Marked as read." });
    }
}
```

---

## 7. Module 6 — Notification & Communication System

### What this module does

- Sends email notifications automatically when key events happen
- Stores every notification in the database for in-app display
- Teachers can manually send announcements to all students in an exam
- Students see unread notification count in their portal

---

### `Services/Notifications/EmailService.cs`

```csharp
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Backend_API.Services.Notifications;

public class EmailService
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IConfiguration config, ILogger<EmailService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(
                _config["Email:SenderName"],
                _config["Email:SenderEmail"]
            ));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(
                _config["Email:SmtpHost"],
                int.Parse(_config["Email:SmtpPort"]!),
                SecureSocketOptions.StartTls
            );
            await client.AuthenticateAsync(
                _config["Email:SenderEmail"],
                _config["Email:AppPassword"]
            );
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            _logger.LogInformation("Email sent to {Email}: {Subject}", toEmail, subject);
        }
        catch (Exception ex)
        {
            // Never crash the main flow because of email failure
            _logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
```

---

### `Services/Notifications/NotificationService.cs`

```csharp
using Backend_API.Data;
using Backend_API.Models.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services.Notifications;

public class NotificationService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;

    public NotificationService(AppDbContext db, EmailService email)
    {
        _db = db;
        _email = email;
    }

    // ── Core method — creates notification + sends email ──────────────────────

    public async Task SendAsync(
        Guid recipientId,
        string title,
        string body,
        string type,
        string? relatedEntityId = null,
        bool sendEmail = true)
    {
        // Save in-app notification
        var notification = new Notification
        {
            RecipientId = recipientId,
            Title = title,
            Body = body,
            Type = type,
            RelatedEntityId = relatedEntityId
        };
        _db.Notifications.Add(notification);

        // Send email if requested
        if (sendEmail)
        {
            var recipient = await _db.Users.FindAsync(recipientId);
            if (recipient is not null && !string.IsNullOrEmpty(recipient.Email))
            {
                var html = BuildEmailHtml(title, body, type);
                await _email.SendAsync(recipient.Email, recipient.Name, $"SmartExam — {title}", html);
                notification.EmailSent = true;
            }
        }

        await _db.SaveChangesAsync();
    }

    // ── Bulk send to many recipients ──────────────────────────────────────────

    public async Task SendToManyAsync(
        IEnumerable<Guid> recipientIds,
        string title,
        string body,
        string type,
        string? relatedEntityId = null,
        bool sendEmail = true)
    {
        foreach (var id in recipientIds)
            await SendAsync(id, title, body, type, relatedEntityId, sendEmail);
    }

    // ── Pre-built notification templates ─────────────────────────────────────

    public async Task NotifyExamScheduledAsync(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Assignments).ThenInclude(a => a.Student)
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return;

        var studentIds = exam.Assignments
            .Where(a => a.IsEligible)
            .Select(a => a.UserId);

        await SendToManyAsync(
            studentIds,
            "Exam Scheduled",
            $"Your exam '{exam.Title}' for {exam.Section.Course.Name} has been scheduled for {exam.StartTime:dddd, dd MMM yyyy} at {exam.StartTime:hh:mm tt}. Duration: {exam.DurationMinutes} minutes.",
            "ExamScheduled",
            examId.ToString()
        );
    }

    public async Task NotifyGradesReleasedAsync(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Sessions)
            .FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return;

        var studentIds = exam.Sessions.Select(s => s.UserId);

        await SendToManyAsync(
            studentIds,
            "Grades Released",
            $"Your grades for '{exam.Title}' are now available. Log in to your SmartExam student portal to view your results and AI feedback.",
            "GradeReleased",
            examId.ToString()
        );
    }

    public async Task NotifyViolationAsync(Guid studentId, string examTitle, string violationType)
    {
        await SendAsync(
            studentId,
            "Exam Violation Recorded",
            $"A violation was recorded during your exam '{examTitle}'. Violation type: {violationType}. This has been reported to your proctor.",
            "ViolationWarning",
            sendEmail: false // violations are in-app only, not email
        );
    }

    public async Task NotifyEligibilityChangedAsync(Guid studentId, string examTitle, bool isNowEligible, string? reason)
    {
        string status = isNowEligible ? "eligible" : "ineligible";
        string reasonText = reason is not null ? $" Reason: {reason}" : string.Empty;

        await SendAsync(
            studentId,
            "Eligibility Updated",
            $"Your eligibility for '{examTitle}' has been updated. You are now {status}.{reasonText}",
            "EligibilityChanged"
        );
    }

    public async Task NotifyExamReminderAsync(Guid examId)
    {
        var exam = await _db.Exams
            .Include(e => e.Assignments)
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .FirstOrDefaultAsync(e => e.ExamId == examId);
        if (exam is null) return;

        var studentIds = exam.Assignments
            .Where(a => a.IsEligible)
            .Select(a => a.UserId);

        await SendToManyAsync(
            studentIds,
            "Exam Reminder — Tomorrow",
            $"Reminder: Your exam '{exam.Title}' for {exam.Section.Course.Name} is tomorrow at {exam.StartTime:hh:mm tt}. Make sure to be at your assigned workstation on time.",
            "ExamReminder",
            examId.ToString()
        );
    }

    // ── Email HTML template ───────────────────────────────────────────────────

    private static string BuildEmailHtml(string title, string body, string type)
    {
        string iconColor = type switch
        {
            "ViolationWarning" => "#DC2626",
            "ExamScheduled"    => "#2563EB",
            "GradeReleased"    => "#16A34A",
            "EligibilityChanged" => "#D97706",
            _                  => "#2563EB"
        };

        return $"""
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"></head>
        <body style="font-family: Arial, sans-serif; background: #F8FAFC; margin: 0; padding: 0;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background: #F8FAFC; padding: 40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background: #FFFFFF; border-radius: 8px; overflow: hidden; border: 1px solid #E2E8F0;">
                <!-- Header -->
                <tr><td style="background: {iconColor}; padding: 24px 32px;">
                  <p style="margin:0; color: #FFFFFF; font-size: 20px; font-weight: bold;">SmartExam</p>
                  <p style="margin:4px 0 0 0; color: rgba(255,255,255,0.8); font-size: 13px;">AI-Driven Lab Exam Monitoring System</p>
                </td></tr>
                <!-- Body -->
                <tr><td style="padding: 32px;">
                  <h2 style="margin: 0 0 16px 0; color: #0F172A; font-size: 18px;">{title}</h2>
                  <p style="margin: 0; color: #475569; font-size: 15px; line-height: 1.6;">{body}</p>
                </td></tr>
                <!-- Footer -->
                <tr><td style="background: #F8FAFC; padding: 16px 32px; border-top: 1px solid #E2E8F0;">
                  <p style="margin: 0; color: #94A3B8; font-size: 12px;">This is an automated message from SmartExam. Do not reply to this email.</p>
                </td></tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;
    }
}
```

---

### `Controllers/Notifications/NotificationsController.cs`

```csharp
using Backend_API.Data;
using Backend_API.Services.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Backend_API.Controllers.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public NotificationsController(AppDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    private Guid Me => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    // GET /api/notifications
    // Returns logged-in user's notifications
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var notifications = await _db.Notifications
            .Where(n => n.RecipientId == Me)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new
            {
                n.NotificationId, n.Title, n.Body,
                n.Type, n.IsRead, n.CreatedAt, n.RelatedEntityId
            })
            .ToListAsync();

        return Ok(new
        {
            UnreadCount = notifications.Count(n => !n.IsRead),
            Notifications = notifications
        });
    }

    // PATCH /api/notifications/{id}/read
    [HttpPatch("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(n => n.NotificationId == id && n.RecipientId == Me);
        if (n is null) return NotFound();
        n.IsRead = true;
        await _db.SaveChangesAsync();
        return Ok();
    }

    // PATCH /api/notifications/read-all
    // Marks all as read in one click
    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var unread = await _db.Notifications
            .Where(n => n.RecipientId == Me && !n.IsRead)
            .ToListAsync();
        unread.ForEach(n => n.IsRead = true);
        await _db.SaveChangesAsync();
        return Ok(new { markedRead = unread.Count });
    }

    // POST /api/notifications/exam/{examId}/announce
    // Teacher sends a manual announcement to all students in an exam
    [HttpPost("exam/{examId:guid}/announce")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> AnnounceToExam(Guid examId, [FromBody] AnnounceRequest req)
    {
        var assignments = await _db.ExamAssignments
            .Where(a => a.ExamId == examId && a.IsEligible)
            .Select(a => a.UserId)
            .ToListAsync();

        if (!assignments.Any())
            return BadRequest(new { message = "No eligible students found for this exam." });

        await _notifications.SendToManyAsync(
            assignments,
            req.Title,
            req.Message,
            "Announcement",
            examId.ToString(),
            sendEmail: req.SendEmail
        );

        return Ok(new { message = $"Announcement sent to {assignments.Count} students." });
    }

    // POST /api/notifications/exam/{examId}/notify-scheduled
    // Called automatically after exam is created — notifies all assigned students
    [HttpPost("exam/{examId:guid}/notify-scheduled")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> NotifyScheduled(Guid examId)
    {
        await _notifications.NotifyExamScheduledAsync(examId);
        return Ok(new { message = "Students notified of exam schedule." });
    }

    // POST /api/notifications/exam/{examId}/notify-grades
    // Called after AI grading completes — notifies students results are ready
    [HttpPost("exam/{examId:guid}/notify-grades")]
    [Authorize(Roles = "Teacher,Admin,SuperAdmin")]
    public async Task<IActionResult> NotifyGrades(Guid examId)
    {
        await _notifications.NotifyGradesReleasedAsync(examId);
        return Ok(new { message = "Students notified of grade release." });
    }
}

public record AnnounceRequest(string Title, string Message, bool SendEmail = false);
```

---

## 8. Wire Everything into Program.cs

Open your existing `Program.cs` and add these lines in the correct positions:

```csharp
// ── Add after existing builder.Services lines ─────────────────────────────

// Module 4 — Analytics
builder.Services.AddScoped<Backend_API.Services.Analytics.AnalyticsService>();

// Module 6 — Notifications
builder.Services.AddScoped<Backend_API.Services.Notifications.EmailService>();
builder.Services.AddScoped<Backend_API.Services.Notifications.NotificationService>();

// Serve static PDF report files
builder.Services.AddDirectoryBrowser();
```

No other changes needed to `Program.cs`. The new controllers are auto-discovered by `app.MapControllers()` which you already have.

---

## 9. Run Migrations

You added 2 new tables (`ExamReports` and `Notifications`). Run migrations to create them in Neon:

```bash
# From inside Backend_API/
dotnet ef migrations add AddModules456
dotnet ef database update
```

Expected output:
```
Build succeeded.
Done.
```

Verify on Neon SQL Editor:
```sql
SELECT table_name FROM information_schema.tables
WHERE table_schema = 'public'
ORDER BY table_name;
```

You should now see `ExamReports` and `Notifications` in the list alongside your existing 20 tables (22 total).

---

## 10. Postman Tests

Run these after all endpoints are live. Set up your Postman collection variables the same way as before — `{{token}}` from admin login, `{{teacherToken}}` from teacher login, `{{studentToken}}` from student login, `{{examId}}` from a seeded exam.

---

### Test M4-1 — Get Exam Summary Analytics

```
GET {{baseUrl}}/api/analytics/exams/{{examId}}/summary
Authorization: Bearer {{teacherToken}}
```

**Expected 200:**
```json
{
  "examId": "...",
  "examTitle": "Mid-Term Lab Exam",
  "totalStudents": 2,
  "averageScore": 0,
  "scoreDistribution": { "0-20": 0, "21-40": 0, ... },
  "questionStats": [ ... ]
}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Has score distribution", () => {
    const b = pm.response.json();
    pm.expect(b.scoreDistribution).to.be.an("object");
    pm.expect(b.questionStats).to.be.an("array");
});
```

---

### Test M4-2 — Get System Analytics (Admin only)

```
GET {{baseUrl}}/api/analytics/system
Authorization: Bearer {{token}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Has student and exam counts", () => {
    const b = pm.response.json();
    pm.expect(b.totalStudents).to.be.a("number");
    pm.expect(b.totalExams).to.be.a("number");
    pm.expect(b.examsByMonth).to.be.an("array");
});
```

---

### Test M4-3 — Teacher Cannot Access System Analytics

```
GET {{baseUrl}}/api/analytics/system
Authorization: Bearer {{teacherToken}}
```

**Test script:**
```javascript
pm.test("Status 403 — teachers cannot see system analytics", () => pm.response.to.have.status(403));
```

---

### Test M4-4 — Generate PDF Report

```
POST {{baseUrl}}/api/analytics/exams/{{examId}}/report
Authorization: Bearer {{teacherToken}}
```

**Expected 200:**
```json
{
  "message": "Report generated.",
  "downloadUrl": "/api/analytics/reports/Exam_Report_....pdf"
}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Download URL returned", () => {
    const b = pm.response.json();
    pm.expect(b.downloadUrl).to.include(".pdf");
    pm.collectionVariables.set("reportUrl", b.downloadUrl);
});
```

---

### Test M4-5 — Download PDF Report

```
GET {{baseUrl}}{{reportUrl}}
Authorization: Bearer {{teacherToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Response is a PDF", () => {
    pm.expect(pm.response.headers.get("Content-Type")).to.include("application/pdf");
});
```

---

### Test M5-1 — Student Dashboard

```
GET {{baseUrl}}/api/student/dashboard
Authorization: Bearer {{studentToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Has required fields", () => {
    const b = pm.response.json();
    pm.expect(b).to.have.property("totalExamsTaken");
    pm.expect(b).to.have.property("averageScore");
    pm.expect(b).to.have.property("performanceTrend");
    pm.expect(b.recentExams).to.be.an("array");
});
```

---

### Test M5-2 — Student Exam List

```
GET {{baseUrl}}/api/student/exams
Authorization: Bearer {{studentToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Student sees their assigned exams", () => {
    const b = pm.response.json();
    pm.expect(b).to.be.an("array");
    pm.expect(b.length).to.be.at.least(1);
});
```

---

### Test M5-3 — Admin Cannot Access Student Portal

```
GET {{baseUrl}}/api/student/dashboard
Authorization: Bearer {{token}}
```

**Test script:**
```javascript
pm.test("Status 403 — admin cannot use student portal", () => pm.response.to.have.status(403));
```

---

### Test M5-4 — Student Performance Trend

```
GET {{baseUrl}}/api/student/performance-trend
Authorization: Bearer {{studentToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Returns array for chart", () => {
    const b = pm.response.json();
    pm.expect(b).to.be.an("array");
});
```

---

### Test M5-5 — Student Violations

```
GET {{baseUrl}}/api/student/violations
Authorization: Bearer {{studentToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Has violation count", () => {
    const b = pm.response.json();
    pm.expect(b).to.have.property("totalViolations");
    pm.expect(b.violations).to.be.an("array");
});
```

---

### Test M6-1 — Get Notifications (empty at first)

```
GET {{baseUrl}}/api/notifications
Authorization: Bearer {{studentToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Has unread count and list", () => {
    const b = pm.response.json();
    pm.expect(b).to.have.property("unreadCount");
    pm.expect(b.notifications).to.be.an("array");
});
```

---

### Test M6-2 — Notify Students of Scheduled Exam

```
POST {{baseUrl}}/api/notifications/exam/{{examId}}/notify-scheduled
Authorization: Bearer {{teacherToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Success message", () => {
    const b = pm.response.json();
    pm.expect(b.message).to.include("notified");
});
```

---

### Test M6-3 — Student Now Has Notifications

Run this immediately after M6-2:

```
GET {{baseUrl}}/api/notifications
Authorization: Bearer {{studentToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Student received exam notification", () => {
    const b = pm.response.json();
    pm.expect(b.unreadCount).to.be.at.least(1);
    pm.expect(b.notifications.length).to.be.at.least(1);
    pm.collectionVariables.set("notificationId", b.notifications[0].notificationId);
});
```

---

### Test M6-4 — Mark Notification as Read

```
PATCH {{baseUrl}}/api/notifications/{{notificationId}}/read
Authorization: Bearer {{studentToken}}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
```

---

### Test M6-5 — Teacher Announcement to Exam Students

```
POST {{baseUrl}}/api/notifications/exam/{{examId}}/announce
Authorization: Bearer {{teacherToken}}
Content-Type: application/json

{
  "title": "Important — Exam Instructions Update",
  "message": "Please note that you are allowed to use the standard library for Q1. Good luck!",
  "sendEmail": false
}
```

**Test script:**
```javascript
pm.test("Status 200", () => pm.response.to.have.status(200));
pm.test("Announcement sent to students", () => {
    const b = pm.response.json();
    pm.expect(b.message).to.include("students");
});
```

---

## Complete Test Run Order for Modules 4–6

```
M4-1  Exam summary analytics         → teacher token
M4-2  System analytics               → admin token
M4-3  Teacher blocked from system    → should 403
M4-4  Generate PDF report            → saves reportUrl variable
M4-5  Download PDF report            → binary PDF response
M5-1  Student dashboard              → student token
M5-2  Student exam list              → at least 1 exam
M5-3  Admin blocked from portal      → should 403
M5-4  Performance trend              → array for chart
M5-5  Student violations             → empty array is fine
M6-1  Get notifications (empty)      → unreadCount: 0
M6-2  Notify exam scheduled          → teacher sends
M6-3  Student has notification       → unreadCount: 1+
M6-4  Mark notification read         → 200
M6-5  Teacher announcement           → sent to all students
```

All 15 pass → **Modules 4, 5, and 6 backend is complete.**
