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
    private readonly ExamAccessService _access;

    public AnalyticsController(AnalyticsService analytics, ExamAccessService access)
    {
        _analytics = analytics;
        _access = access;
    }

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

    // GET /api/analytics/reports/{fileName}
    // Downloads the generated PDF file
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

        var outputDir = Path.GetFullPath("Reports/");
        var filePath = Path.GetFullPath(Path.Combine(outputDir, safeName));
        if (!filePath.StartsWith(outputDir, StringComparison.Ordinal) || !System.IO.File.Exists(filePath))
            return NotFound(new { message = "Report not found." });

        var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(bytes, "application/pdf", safeName);
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
