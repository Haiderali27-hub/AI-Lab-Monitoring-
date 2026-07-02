using Backend_API.Data;
using Backend_API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Controllers;

/// <summary>
/// Admin maintenance utilities. The reset endpoint wipes ALL data and reseeds the
/// minimal demo set — useful before a fresh demo run. Guarded to SuperAdmin/Admin.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AdminMaintenanceController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;

    public AdminMaintenanceController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
    }

    // POST /api/admin/reset-demo-data  — wipe everything and reseed the minimal demo set
    [HttpPost("reset-demo-data")]
    public async Task<IActionResult> ResetDemoData()
    {
        var actorId = Guid.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

        // Delete in FK-safe order
        _db.AuditLogs.RemoveRange(_db.AuditLogs);
        _db.Notifications.RemoveRange(_db.Notifications);
        _db.ExamReports.RemoveRange(_db.ExamReports);
        _db.PlagiarismResults.RemoveRange(_db.PlagiarismResults);
        _db.TeacherGradeOverrides.RemoveRange(_db.TeacherGradeOverrides);
        _db.AiGradingResults.RemoveRange(_db.AiGradingResults);
        _db.Answers.RemoveRange(_db.Answers);
        _db.MonitoringEvents.RemoveRange(_db.MonitoringEvents);
        _db.ExamSessions.RemoveRange(_db.ExamSessions);
        _db.ExamAssignments.RemoveRange(_db.ExamAssignments);
        _db.TestCases.RemoveRange(_db.TestCases);
        _db.Questions.RemoveRange(_db.Questions);
        _db.Exams.RemoveRange(_db.Exams);
        _db.SectionEnrollments.RemoveRange(_db.SectionEnrollments);
        _db.Sections.RemoveRange(_db.Sections);
        _db.Courses.RemoveRange(_db.Courses);
        _db.Departments.RemoveRange(_db.Departments);
        _db.Workstations.RemoveRange(_db.Workstations);
        _db.Labs.RemoveRange(_db.Labs);
        _db.UserSessions.RemoveRange(_db.UserSessions);
        _db.DeviceBindings.RemoveRange(_db.DeviceBindings);
        _db.Users.RemoveRange(_db.Users);
        await _db.SaveChangesAsync();

        await DbSeeder.SeedAsync(_db);

        return Ok(new { message = "Demo data reset. All users must log in again." });
    }
}
