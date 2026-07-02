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

    public AppDbContext Db => _db;

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
