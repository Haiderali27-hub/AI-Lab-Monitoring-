using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend_API.Data;
using Backend_API.Models;
using Backend_API.Models.Analytics;
using Backend_API.Models.Enums;
using Backend_API.Services.Monitoring;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PdfImage = QuestPDF.Infrastructure.Image;

namespace Backend_API.Services.Analytics;

/// <summary>
/// Module 12 — per-exam exports: attendance, violations (with screenshot evidence) and scores as PDF (QuestPDF),
/// scores + attendance as Excel (ClosedXML), and an evidence ZIP (screenshots, events CSV, incidents, the exam's
/// audit extract and a manifest with the SHA-256 of every file). Files go to the report folder like the exam summary
/// PDF, are listed in ExamReports and downloaded through <c>GET /api/analytics/reports/{fileName}</c>; each export is
/// audited as <c>REPORT_GENERATED</c> with the file's SHA-256, so the hash chain also vouches for what was handed out.
/// </summary>
public sealed class ReportExportService(AppDbContext db, AnalyticsService analytics, EvidenceStorage evidence, AuditService audit)
{
    public const string Attendance = "attendance";
    public const string Violations = "violations";
    public const string Scores = "scores";
    public const string Excel = "excel";
    public const string EvidenceZip = "evidence-zip";
    public static readonly string[] Types = [Attendance, Violations, Scores, Excel, EvidenceZip];

    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Evidence thumbnails in the violations PDF: a few per student, bounded overall so the file stays mailable.
    private const int MaxThumbnailsPerStudent = 6;
    private const int MaxThumbnailsPerReport = 48;
    private const int MaxViolationRowsPerStudent = 100;

    public sealed record GeneratedReport(string FileName, string ReportType, string Sha256);

    /// <summary>Content type of a generated report file, from its extension.</summary>
    public static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".xlsx" => XlsxContentType,
        ".zip" => "application/zip",
        _ => "application/pdf"
    };

    public async Task<GeneratedReport> GenerateAsync(string type, Guid examId, Guid requestedBy, CancellationToken cancellationToken = default)
    {
        var data = await LoadAsync(examId, cancellationToken);
        var (reportType, prefix, extension) = type switch
        {
            Attendance => ("Attendance", "Attendance_Report", ".pdf"),
            Violations => ("Violations", "Violations_Report", ".pdf"),
            Scores => ("Scores", "Scores_Report", ".pdf"),
            Excel => ("ScoresExcel", "Scores", ".xlsx"),
            EvidenceZip => ("EvidenceZip", "Evidence", ".zip"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown report type.")
        };

        var outputDir = analytics.ReportDirectory;
        Directory.CreateDirectory(outputDir);
        var fileName = $"{prefix}_{examId}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid().ToString("N")[..6]}{extension}";
        var filePath = Path.Combine(outputDir, fileName);

        switch (type)
        {
            case Attendance: AttendancePdf(data).GeneratePdf(filePath); break;
            case Violations: ViolationsPdf(data).GeneratePdf(filePath); break;
            case Scores: ScoresPdf(data).GeneratePdf(filePath); break;
            case Excel: ScoresWorkbook(data, filePath); break;
            default: await EvidenceZipAsync(data, filePath, requestedBy, cancellationToken); break;
        }

        string sha;
        await using (var file = File.OpenRead(filePath))
            sha = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();

        db.ExamReports.Add(new ExamReport { ExamId = examId, GeneratedBy = requestedBy, ReportType = reportType, FilePath = filePath });
        audit.Add(requestedBy, "REPORT_GENERATED", "Exam", examId.ToString(), new
        {
            reportType,
            fileName,
            sha256 = sha,
            sizeBytes = new FileInfo(filePath).Length
        });
        await db.SaveChangesAsync(cancellationToken);
        return new GeneratedReport(fileName, reportType, sha);
    }

    // ── Data ─────────────────────────────────────────────────────────────────

    private sealed record StudentRow(
        User Student,
        ExamAssignment Assignment,
        ExamSession? Session,
        AttendanceRecord? ClassAttendance,
        List<MonitoringEvent> Violations,
        double? Score,
        Dictionary<Guid, double> MarksByQuestion)
    {
        public string Status => Session is null
            ? (Assignment.IsEligible ? "Absent" : "Not eligible")
            : Session.Status switch
            {
                SessionStatus.InProgress => "In progress",
                SessionStatus.Submitted => "Submitted",
                SessionStatus.ForceSubmitted => "Force submitted",
                SessionStatus.TimedOut => "Timed out",
                _ => Session.Status.ToString()
            };

        public double? AttendancePercent => ClassAttendance is { TotalClasses: > 0 } a
            ? Math.Round(a.AttendedClasses * 100.0 / a.TotalClasses, 1)
            : null;
    }

    private sealed record ReportData(
        Exam Exam,
        List<Question> Questions,
        int TotalMarks,
        List<StudentRow> Students,
        List<Screenshot> Screenshots,
        List<Incident> Incidents);

    private async Task<ReportData> LoadAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await db.Exams.AsNoTracking()
            .Include(e => e.Section).ThenInclude(s => s.Course)
            .Include(e => e.Questions)
            .FirstOrDefaultAsync(e => e.ExamId == examId, cancellationToken)
            ?? throw new KeyNotFoundException("Exam not found.");
        var questions = exam.Questions.OrderBy(q => q.OrderIndex).ToList();

        var assignments = await db.ExamAssignments.AsNoTracking()
            .Include(a => a.Student)
            .Include(a => a.Workstation)
            .Where(a => a.ExamId == examId)
            .ToListAsync(cancellationToken);

        // A student's latest session of the exam (there is normally one: no re-sit, D-43).
        var sessions = (await db.ExamSessions.AsNoTracking()
                .Include(s => s.Answers).ThenInclude(a => a.AiGradingResult)
                .Include(s => s.Answers).ThenInclude(a => a.TeacherGradeOverride)
                .Where(s => s.ExamId == examId)
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.StartedAt).First());
        var sessionIds = sessions.Values.Select(s => s.SessionId).ToList();

        var violations = (await db.MonitoringEvents.AsNoTracking()
                .Where(e => sessionIds.Contains(e.ExamSessionId) && ViolationClassifier.ViolationTypes.Contains(e.EventType))
                .ToListAsync(cancellationToken))
            .ToLookup(e => e.ExamSessionId);

        var studentIds = assignments.Select(a => a.UserId).ToList();
        var attendance = (await db.AttendanceRecords.AsNoTracking()
                .Where(r => r.SectionId == exam.SectionId && studentIds.Contains(r.UserId))
                .ToListAsync(cancellationToken))
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.UpdatedAt).First());

        var rows = assignments
            .Select(a =>
            {
                var session = sessions.GetValueOrDefault(a.UserId);
                var marks = new Dictionary<Guid, double>();
                double? score = null;
                if (session is not null && session.Status != SessionStatus.InProgress)
                {
                    foreach (var answer in session.Answers)
                        marks[answer.QuestionId] = answer.TeacherGradeOverride?.FinalMarks ?? answer.AiGradingResult?.SuggestedMarks ?? 0;
                    score = marks.Values.Sum();
                }
                return new StudentRow(a.Student, a, session, attendance.GetValueOrDefault(a.UserId),
                    session is null ? [] : violations[session.SessionId].OrderBy(e => e.RecordedAt).ToList(), score, marks);
            })
            .OrderBy(r => r.Student.RollNumber ?? "~").ThenBy(r => r.Student.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var screenshots = await db.Screenshots.AsNoTracking()
            .Where(s => s.ExamId == examId)
            .OrderBy(s => s.CapturedAt)
            .ToListAsync(cancellationToken);
        var incidents = await db.Incidents.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

        return new ReportData(exam, questions, questions.Sum(q => q.Marks), rows, screenshots, incidents);
    }

    // ── Formatting helpers ───────────────────────────────────────────────────

    private static string Time(DateTime? utc) => utc is DateTime t ? t.ToString("dd MMM yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture) : "—";
    private static string Num(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Percent(double? score, int total) => score is double s && total > 0 ? Num(Math.Round(s * 100 / total, 1)) + "%" : "—";
    private static string RollOf(User u) => string.IsNullOrWhiteSpace(u.RollNumber) ? "—" : u.RollNumber!;

    private static string Short(string? text, int max) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..(max - 1)] + "…";

    private static Document Pdf(ReportData data, string title, Action<ColumnDescriptor> content) =>
        Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(36);
            page.DefaultTextStyle(x => x.FontFamily("Arial").FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Text($"SmartExam — {title}").FontSize(18).Bold().FontColor(Colors.Blue.Medium);
                col.Item().Text(data.Exam.Title).FontSize(13).Bold();
                col.Item().Text($"{data.Exam.Section.Course.Name} • Section {data.Exam.Section.Name} • " +
                                $"Starts {Time(data.Exam.StartTime)} • {data.Exam.DurationMinutes} min • Generated {Time(DateTime.UtcNow)}")
                    .FontColor(Colors.Grey.Medium);
                col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Blue.Medium);
            });
            page.Content().Column(content);
            page.Footer().AlignCenter().Text(x =>
            {
                x.Span("SmartExam — Confidential Report • Page ");
                x.CurrentPageNumber();
                x.Span(" of ");
                x.TotalPages();
            });
        }));

    private static void Table(ColumnDescriptor col, string[] headers, float[] widths, IEnumerable<string[]> rows)
    {
        col.Item().Table(table =>
        {
            table.ColumnsDefinition(c => { foreach (var w in widths) c.RelativeColumn(w); });
            table.Header(h =>
            {
                foreach (var header in headers)
                    h.Cell().Background(Colors.Blue.Medium).Padding(4).Text(header).Bold().FontColor(Colors.White);
            });
            var any = false;
            foreach (var row in rows)
            {
                any = true;
                foreach (var cell in row)
                    table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(cell);
            }
            if (!any)
                table.Cell().ColumnSpan((uint)headers.Length).Padding(6).Text("None.").Italic().FontColor(Colors.Grey.Medium);
        });
    }

    private static void Stats(ColumnDescriptor col, params (string Label, string Value)[] stats)
    {
        col.Item().PaddingBottom(10).Table(table =>
        {
            table.ColumnsDefinition(c => { foreach (var _ in stats) c.RelativeColumn(); });
            foreach (var (label, value) in stats)
                table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(6).Column(c =>
                {
                    c.Item().Text(label).FontColor(Colors.Grey.Medium).FontSize(8);
                    c.Item().Text(value).Bold().FontSize(12);
                });
        });
    }

    // ── Attendance PDF ───────────────────────────────────────────────────────

    private static Document AttendancePdf(ReportData data) => Pdf(data, "Attendance Report", col =>
    {
        var sat = data.Students.Count(s => s.Session is not null);
        Stats(col,
            ("Assigned", data.Students.Count.ToString(CultureInfo.InvariantCulture)),
            ("Eligible", data.Students.Count(s => s.Assignment.IsEligible).ToString(CultureInfo.InvariantCulture)),
            ("Attended", sat.ToString(CultureInfo.InvariantCulture)),
            ("Submitted", data.Students.Count(s => s.Session is { Status: not SessionStatus.InProgress }).ToString(CultureInfo.InvariantCulture)),
            ("Absent", (data.Students.Count - sat).ToString(CultureInfo.InvariantCulture)));

        Table(col,
            ["#", "Student", "Roll no.", "Seat", "Eligible", "Class attendance", "Status", "Started", "Submitted"],
            [0.4f, 2.2f, 1.3f, 0.8f, 0.8f, 1f, 1.2f, 1.6f, 1.6f],
            data.Students.Select((s, i) => new[]
            {
                (i + 1).ToString(CultureInfo.InvariantCulture),
                s.Student.Name,
                RollOf(s.Student),
                s.Assignment.Workstation?.MachineNumber ?? "—",
                s.Assignment.IsEligible ? "Yes" : "No" + (s.Assignment.EligibilityOverridden ? " (override)" : ""),
                s.AttendancePercent is double p ? Num(p) + "%" : "—",
                s.Status,
                Time(s.Session?.StartedAt),
                Time(s.Session?.SubmittedAt)
            }));
    });

    // ── Scores PDF ───────────────────────────────────────────────────────────

    private static Document ScoresPdf(ReportData data) => Pdf(data, "Scores Report", col =>
    {
        var graded = data.Students.Where(s => s.Score is not null).Select(s => s.Score!.Value).ToList();
        Stats(col,
            ("Graded", graded.Count.ToString(CultureInfo.InvariantCulture)),
            ("Total marks", data.TotalMarks.ToString(CultureInfo.InvariantCulture)),
            ("Average", graded.Count == 0 ? "—" : Percent(graded.Average(), data.TotalMarks)),
            ("Highest", graded.Count == 0 ? "—" : Percent(graded.Max(), data.TotalMarks)),
            ("Lowest", graded.Count == 0 ? "—" : Percent(graded.Min(), data.TotalMarks)));

        var questionHeaders = data.Questions.Select((q, i) => $"Q{i + 1} /{q.Marks}").ToArray();
        Table(col,
            ["Student", "Roll no.", "Status", .. questionHeaders, "Total", "%", "Violations"],
            [2.2f, 1.3f, 1.2f, .. questionHeaders.Select(_ => 0.7f), 0.8f, 0.8f, 0.9f],
            data.Students.Select(s => (string[])
            [
                s.Student.Name,
                RollOf(s.Student),
                s.Status,
                .. data.Questions.Select(q => s.Score is null ? "—" : Num(s.MarksByQuestion.GetValueOrDefault(q.QuestionId))),
                s.Score is double score ? $"{Num(score)} / {data.TotalMarks}" : "—",
                Percent(s.Score, data.TotalMarks),
                s.Violations.Count.ToString(CultureInfo.InvariantCulture)
            ]));
        col.Item().PaddingTop(8).Text("Marks are the teacher's override where one exists, else the AI suggestion; a pending or failed grading counts 0.")
            .FontSize(8).FontColor(Colors.Grey.Medium);
    });

    // ── Violations PDF (with evidence thumbnails) ────────────────────────────

    private Document ViolationsPdf(ReportData data)
    {
        // Load the thumbnails first: a file that is gone (retention) or not a decodable image is listed, not drawn.
        var budget = MaxThumbnailsPerReport;
        var thumbnails = new Dictionary<Guid, List<(Screenshot Shot, PdfImage Image)>>();
        foreach (var row in data.Students.Where(s => s.Session is not null && s.Violations.Count > 0))
        {
            var list = new List<(Screenshot, PdfImage)>();
            var candidates = data.Screenshots
                .Where(s => s.SessionId == row.Session!.SessionId)
                .OrderBy(s => s.Trigger == Screenshot.TriggerViolation ? 0 : 1)
                .ThenBy(s => s.CapturedAt);
            foreach (var shot in candidates)
            {
                if (list.Count >= MaxThumbnailsPerStudent || budget <= 0) break;
                var bytes = ReadEvidence(shot.StoragePath);
                if (bytes is null) continue;
                try
                {
                    list.Add((shot, PdfImage.FromBinaryData(bytes)));
                    budget--;
                }
                catch (Exception)
                {
                    // Not a decodable image: skip it (the evidence ZIP still carries the file).
                }
            }
            thumbnails[row.Session!.SessionId] = list;
        }

        return Pdf(data, "Violations Report", col =>
        {
            var flagged = data.Students.Where(s => s.Violations.Count > 0).ToList();
            Stats(col,
                ("Students with violations", flagged.Count.ToString(CultureInfo.InvariantCulture)),
                ("Violation events", flagged.Sum(s => s.Violations.Count).ToString(CultureInfo.InvariantCulture)),
                ("Incidents", data.Incidents.Count.ToString(CultureInfo.InvariantCulture)),
                ("Screenshots on file", data.Screenshots.Count.ToString(CultureInfo.InvariantCulture)));

            col.Item().PaddingBottom(4).Text("Summary").Bold().FontSize(12);
            Table(col,
                ["Student", "Roll no.", "Classification", "Events", "Most frequent"],
                [2.2f, 1.3f, 1.2f, 0.8f, 2.5f],
                flagged.Select(s => new[]
                {
                    s.Student.Name,
                    RollOf(s.Student),
                    ViolationClassifier.Classify(s.Violations.Select(v => v.EventType)),
                    s.Violations.Count.ToString(CultureInfo.InvariantCulture),
                    string.Join(", ", s.Violations.GroupBy(v => v.EventType).OrderByDescending(g => g.Count()).Take(3)
                        .Select(g => $"{g.Key} ×{g.Count()}"))
                }));

            var studentsBySession = data.Students.Where(s => s.Session is not null).ToDictionary(s => s.Session!.SessionId);
            col.Item().PaddingTop(12).PaddingBottom(4).Text("Incidents").Bold().FontSize(12);
            Table(col,
                ["Opened", "Student", "Severity", "Status", "Notes"],
                [1.6f, 2f, 0.9f, 0.9f, 3.5f],
                data.Incidents.Select(i => new[]
                {
                    Time(i.CreatedAt),
                    studentsBySession.TryGetValue(i.SessionId, out var s) ? s.Student.Name : "—",
                    i.Severity.ToString(),
                    i.Status.ToString(),
                    Short(i.Notes, 300)
                }));

            foreach (var row in flagged)
            {
                col.Item().PageBreak();
                col.Item().PaddingBottom(4).Text($"{row.Student.Name} ({RollOf(row.Student)}) — {row.Violations.Count} violation event(s)")
                    .Bold().FontSize(12);
                Table(col,
                    ["Recorded", "Type", "Severity", "Details"],
                    [1.6f, 1.4f, 0.9f, 4f],
                    row.Violations.Take(MaxViolationRowsPerStudent).Select(v => new[]
                    {
                        v.RecordedAt.ToString("dd MMM HH:mm:ss", CultureInfo.InvariantCulture),
                        v.EventType.ToString(),
                        ViolationClassifier.SeverityName(v.EventType),
                        Short(v.Payload, 160)
                    }));
                if (row.Violations.Count > MaxViolationRowsPerStudent)
                    col.Item().Text($"… and {row.Violations.Count - MaxViolationRowsPerStudent} more (see the evidence ZIP).")
                        .FontSize(8).FontColor(Colors.Grey.Medium);

                var shots = thumbnails.GetValueOrDefault(row.Session!.SessionId) ?? [];
                col.Item().PaddingTop(8).Text(shots.Count == 0 ? "No screenshot evidence on file." : "Screenshot evidence")
                    .Bold().FontColor(shots.Count == 0 ? Colors.Grey.Medium : Colors.Black);
                if (shots.Count > 0)
                {
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                        foreach (var (shot, image) in shots)
                            table.Cell().Padding(3).Column(c =>
                            {
                                c.Item().Height(110).Image(image).FitArea();
                                c.Item().Text($"{shot.Kind} • {shot.Trigger}{(shot.TriggerReason is null ? "" : " (" + shot.TriggerReason + ")")} • " +
                                              shot.CapturedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                                    .FontSize(7).FontColor(Colors.Grey.Darken1);
                            });
                    });
                }
            }
        });
    }

    private byte[]? ReadEvidence(string relativePath)
    {
        using var stream = evidence.OpenRead(relativePath);
        if (stream is null) return null;
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    // ── Excel ────────────────────────────────────────────────────────────────

    private static void ScoresWorkbook(ReportData data, string filePath)
    {
        using var workbook = new XLWorkbook();

        // Sheet 1 — Scores: a header row, then exactly one row per assigned student.
        var scores = workbook.Worksheets.Add("Scores");
        var headers = new List<string> { "Student", "Roll number", "Email", "Eligible", "Status", "Started (UTC)", "Submitted (UTC)" };
        headers.AddRange(data.Questions.Select((q, i) => $"Q{i + 1} (/{q.Marks})"));
        headers.AddRange(["Total", "Max", "Percent", "Violations"]);
        for (var c = 0; c < headers.Count; c++) scores.Cell(1, c + 1).Value = headers[c];
        scores.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var s in data.Students)
        {
            var c = 1;
            scores.Cell(r, c++).Value = s.Student.Name;
            scores.Cell(r, c++).Value = s.Student.RollNumber ?? "";
            scores.Cell(r, c++).Value = s.Student.Email;
            scores.Cell(r, c++).Value = s.Assignment.IsEligible ? "Yes" : "No";
            scores.Cell(r, c++).Value = s.Status;
            SetTime(scores.Cell(r, c++), s.Session?.StartedAt);
            SetTime(scores.Cell(r, c++), s.Session?.SubmittedAt);
            foreach (var q in data.Questions)
            {
                if (s.Score is not null) scores.Cell(r, c).Value = s.MarksByQuestion.GetValueOrDefault(q.QuestionId);
                c++;
            }
            if (s.Score is double score)
            {
                scores.Cell(r, c).Value = score;
                scores.Cell(r, c + 2).Value = data.TotalMarks > 0 ? Math.Round(score * 100 / data.TotalMarks, 1) : 0;
            }
            scores.Cell(r, c + 1).Value = data.TotalMarks;
            scores.Cell(r, c + 3).Value = s.Violations.Count;
            r++;
        }
        scores.SheetView.FreezeRows(1);
        scores.Columns().AdjustToContents();

        // Sheet 2 — Attendance.
        var attendance = workbook.Worksheets.Add("Attendance");
        string[] aHeaders = ["Student", "Roll number", "Seat", "Eligible", "Eligibility note", "Class attendance %", "Status", "Started (UTC)", "Submitted (UTC)"];
        for (var c = 0; c < aHeaders.Length; c++) attendance.Cell(1, c + 1).Value = aHeaders[c];
        attendance.Row(1).Style.Font.Bold = true;
        r = 2;
        foreach (var s in data.Students)
        {
            attendance.Cell(r, 1).Value = s.Student.Name;
            attendance.Cell(r, 2).Value = s.Student.RollNumber ?? "";
            attendance.Cell(r, 3).Value = s.Assignment.Workstation?.MachineNumber ?? "";
            attendance.Cell(r, 4).Value = s.Assignment.IsEligible ? "Yes" : "No";
            attendance.Cell(r, 5).Value = s.Assignment.EligibilityNote ?? "";
            if (s.AttendancePercent is double p) attendance.Cell(r, 6).Value = p;
            attendance.Cell(r, 7).Value = s.Status;
            SetTime(attendance.Cell(r, 8), s.Session?.StartedAt);
            SetTime(attendance.Cell(r, 9), s.Session?.SubmittedAt);
            r++;
        }
        attendance.SheetView.FreezeRows(1);
        attendance.Columns().AdjustToContents();

        workbook.SaveAs(filePath);
    }

    private static void SetTime(IXLCell cell, DateTime? utc)
    {
        if (utc is not DateTime t) return;
        cell.Value = t;
        cell.Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
    }

    // ── Evidence ZIP ─────────────────────────────────────────────────────────

    private async Task EvidenceZipAsync(ReportData data, string filePath, Guid requestedBy, CancellationToken cancellationToken)
    {
        var examId = data.Exam.ExamId;
        var sessions = data.Students.Where(s => s.Session is not null).ToDictionary(s => s.Session!.SessionId);
        var sessionIds = sessions.Keys.ToList();
        var sessionKeys = sessionIds.Select(id => id.ToString()).ToList();
        var answerIds = data.Students.Where(s => s.Session is not null)
            .SelectMany(s => s.Session!.Answers.Select(a => a.AnswerId.ToString())).ToList();

        var files = new List<object>();
        var missing = new List<object>();

        await using var output = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write);
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, byte[] bytes, CompressionLevel level = CompressionLevel.Optimal, object? extra = null)
            {
                var entry = zip.CreateEntry(path, level);
                using (var s = entry.Open()) s.Write(bytes);
                files.Add(new
                {
                    path,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                    sizeBytes = bytes.Length,
                    source = extra
                });
            }

            // Every monitoring event of the exam's sessions (heartbeats included: they show when a PC was alive).
            var events = await db.MonitoringEvents.AsNoTracking()
                .Where(e => sessionIds.Contains(e.ExamSessionId))
                .OrderBy(e => e.RecordedAt)
                .ToListAsync(cancellationToken);
            var csv = new StringBuilder("recordedAtUtc,receivedAtUtc,student,rollNumber,sessionId,eventType,severity,clientEventId,eventId,payload\r\n");
            foreach (var e in events)
            {
                var s = sessions[e.ExamSessionId].Student;
                Csv(csv, Iso(e.RecordedAt), Iso(e.ReceivedAt), s.Name, s.RollNumber, e.ExamSessionId.ToString(), e.EventType.ToString(),
                    ViolationClassifier.SeverityName(e.EventType), e.ClientEventId?.ToString(), e.EventId.ToString(), e.Payload);
            }
            Add("events.csv", Utf8(csv));

            var incidents = new StringBuilder("createdAtUtc,student,sessionId,severity,status,notes,statusNote,resolvedAtUtc,incidentId\r\n");
            foreach (var i in data.Incidents)
                Csv(incidents, Iso(i.CreatedAt), sessions.TryGetValue(i.SessionId, out var row) ? row.Student.Name : null, i.SessionId.ToString(),
                    i.Severity.ToString(), i.Status.ToString(), i.Notes, i.StatusNote, Iso(i.ResolvedAt), i.IncidentId.ToString());
            Add("incidents.csv", Utf8(incidents));

            // The exam's slice of the audit chain, with sequence and hashes so it can be checked against the full chain.
            var examKey = examId.ToString();
            var auditRows = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityId == examKey
                            || (a.EntityType == "ExamSession" && a.EntityId != null && sessionKeys.Contains(a.EntityId))
                            || (a.EntityType == "Answer" && a.EntityId != null && answerIds.Contains(a.EntityId)))
                .OrderBy(a => a.Sequence ?? long.MaxValue).ThenBy(a => a.CreatedAt)
                .ToListAsync(cancellationToken);
            var auditCsv = new StringBuilder("sequence,createdAtUtc,eventType,actorId,entityType,entityId,details,prevHash,hash\r\n");
            foreach (var a in auditRows)
                Csv(auditCsv, a.Sequence?.ToString(CultureInfo.InvariantCulture), Iso(a.CreatedAt), a.EventType, a.ActorId?.ToString(),
                    a.EntityType, a.EntityId, a.Details, a.PrevHash, a.Hash);
            Add("audit.csv", Utf8(auditCsv));

            // Screenshots and webcam snapshots, one folder per student.
            foreach (var shot in data.Screenshots)
            {
                var bytes = ReadEvidence(shot.StoragePath);
                if (bytes is null)
                {
                    missing.Add(new { screenshotId = shot.ScreenshotId, shot.SessionId, shot.CapturedAt, reason = "file no longer on the server (retention)" });
                    continue;
                }
                var student = sessions.TryGetValue(shot.SessionId, out var owner) ? owner.Student : null;
                var folder = SafeName(student is null ? shot.SessionId.ToString() : $"{student.RollNumber ?? student.Name}_{student.UserId.ToString()[..8]}");
                var path = $"screenshots/{folder}/{shot.CapturedAt:yyyyMMdd_HHmmss}_{shot.Kind}_{shot.Trigger}_{shot.ScreenshotId.ToString()[..8]}{EvidenceStorage.ExtensionFor(shot.ContentType)}";
                var actualSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                Add(path, bytes, CompressionLevel.NoCompression, new
                {
                    screenshotId = shot.ScreenshotId,
                    shot.SessionId,
                    shot.CapturedAt,
                    shot.Kind,
                    shot.Trigger,
                    shot.TriggerReason,
                    uploadSha256 = shot.Sha256,
                    matchesUpload = string.Equals(actualSha, shot.Sha256, StringComparison.OrdinalIgnoreCase)
                });
            }

            var chain = await AuditChain.VerifyAsync(db, cancellationToken);
            var manifest = new
            {
                examId,
                examTitle = data.Exam.Title,
                course = data.Exam.Section.Course.Name,
                section = data.Exam.Section.Name,
                generatedAtUtc = DateTime.UtcNow,
                generatedBy = requestedBy,
                auditChain = new { chain.Valid, chain.BrokenAt, chain.LastVerifiedSequence, chain.LastVerifiedHash },
                files,
                missingScreenshots = missing
            };
            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            await using var ms = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(ms, manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }, cancellationToken);
        }
    }

    private static string Iso(DateTime? utc) => utc?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) ?? "";

    private static byte[] Utf8(StringBuilder sb) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble()
        .Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

    // RFC 4180 quoting; a leading = + - @ is prefixed with ' so a spreadsheet does not run it as a formula.
    private static void Csv(StringBuilder sb, params string?[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var v = values[i] ?? "";
            if (v.Length > 0 && "=+-@".Contains(v[0]) && !double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                v = "'" + v;
            sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
        }
        sb.Append("\r\n");
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) || ch == ' ' ? '_' : ch).ToArray());
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}
