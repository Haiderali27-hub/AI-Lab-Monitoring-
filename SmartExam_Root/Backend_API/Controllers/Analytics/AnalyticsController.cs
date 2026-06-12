using Backend_API.Services.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
