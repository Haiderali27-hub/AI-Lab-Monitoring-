using Backend_API.Services;
using Backend_API.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Backend_API.Controllers.Analytics;

/// <summary>
/// Module 12 analytics and PDF reports. An exam's statistics and reports carry its students' scores, so they are the
/// exam owner's (section teacher) or an admin's, like the results page (D-34 / D-39): 404 for an unknown exam, 403 for
/// another teacher; a report file is checked through the exam it was generated for.
/// </summary>
[ApiController]
[Route("api/analytics")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private const string NotOwnerMessage = "Only the exam's teacher or an admin can see its analytics and reports.";

    private readonly AnalyticsService _analytics;
    private readonly ReportExportService _exports;
    private readonly ExamAccessService _access;

    public AnalyticsController(AnalyticsService analytics, ReportExportService exports, ExamAccessService access)
    {
        _analytics = analytics;
        _exports = exports;
        _access = access;
    }

    public sealed record ReportRequest(Guid ExamId);

    private Task<IActionResult?> DenyUnlessOwnerAsync(Guid examId) =>
        _access.DenyUnlessAsync(User, examId, ExamAccess.Owner, NotOwnerMessage);

    // GET /api/analytics/exams/{examId}/summary
    // Used by: Teacher Results page charts, Admin exam detail
    [HttpGet("exams/{examId:guid}/summary")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GetExamSummary(Guid examId)
    {
        var denied = await DenyUnlessOwnerAsync(examId);
        if (denied is not null) return denied;
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
        var denied = await DenyUnlessOwnerAsync(examId);
        if (denied is not null) return denied;

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var filePath = await _analytics.GenerateExamReportPdfAsync(examId, userId);
        var fileName = Path.GetFileName(filePath);
        return Ok(new { message = "Report generated.", downloadUrl = $"/api/analytics/reports/{fileName}" });
    }

    // POST /api/analytics/reports/{type}  body { examId }  — Module 12 exports:
    // attendance | violations | scores (PDF), excel (scores + attendance .xlsx), evidence-zip (screenshots, events CSV,
    // incidents, audit extract, manifest with SHA-256 per file). Returns the download URL like the exam report.
    [HttpPost("reports/{type}")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GenerateExport(string type, [FromBody] ReportRequest request, CancellationToken cancellationToken)
    {
        var kind = type.Trim().ToLowerInvariant();
        if (!ReportExportService.Types.Contains(kind))
            return BadRequest(new { message = $"Unknown report type. Use one of: {string.Join(", ", ReportExportService.Types)}." });
        if (request.ExamId == Guid.Empty)
            return BadRequest(new { message = "examId is required." });

        var denied = await DenyUnlessOwnerAsync(request.ExamId);
        if (denied is not null) return denied;

        var userId = Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var report = await _exports.GenerateAsync(kind, request.ExamId, userId, cancellationToken);
        return Ok(new
        {
            message = "Report generated.",
            report.ReportType,
            report.FileName,
            report.Sha256,
            downloadUrl = $"/api/analytics/reports/{report.FileName}"
        });
    }

    // GET /api/analytics/reports/{fileName}
    // Downloads a generated report file (PDF, Excel or evidence ZIP)
    [HttpGet("reports/{fileName}")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> DownloadReport(string fileName)
    {
        // Guard against path traversal: only allow a bare file name, no directory parts.
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName)
            return BadRequest(new { message = "Invalid file name." });

        // The file belongs to the exam it was generated for: only that exam's owner (or an admin) downloads it.
        var candidates = await _analytics.Db.ExamReports.AsNoTracking()
            .Where(r => r.FilePath.EndsWith(safeName))
            .Select(r => new { r.ExamId, r.FilePath })
            .ToListAsync();
        var report = candidates.FirstOrDefault(r => Path.GetFileName(r.FilePath) == safeName);
        if (report is null) return NotFound(new { message = "Report not found." });
        var denied = await DenyUnlessOwnerAsync(report.ExamId);
        if (denied is not null) return denied;

        var outputDir = _analytics.ReportDirectory;
        if (!outputDir.EndsWith(Path.DirectorySeparatorChar)) outputDir += Path.DirectorySeparatorChar;
        var filePath = Path.GetFullPath(Path.Combine(outputDir, safeName));
        if (!filePath.StartsWith(outputDir, StringComparison.Ordinal) || !System.IO.File.Exists(filePath))
            return NotFound(new { message = "Report not found." });

        // Streamed: an evidence ZIP can be large.
        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return File(stream, ReportExportService.ContentTypeFor(safeName), safeName);
    }

    // GET /api/analytics/exams/{examId}/reports
    // Lists previously generated reports for an exam
    [HttpGet("exams/{examId:guid}/reports")]
    [Authorize(Roles = "Admin,SuperAdmin,Teacher")]
    public async Task<IActionResult> GetReportHistory(Guid examId)
    {
        var denied = await DenyUnlessOwnerAsync(examId);
        if (denied is not null) return denied;

        var reports = await _analytics.Db.ExamReports
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
