using System.Security.Claims;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services;

/// <summary>How much a staff member may do with one exam. Ordered: a higher level includes the lower ones.</summary>
public enum ExamAccess
{
    /// <summary>No access (students, unrelated teachers, unassigned invigilators, unknown exam).</summary>
    None = 0,
    /// <summary>Assigned through an <see cref="ExamProctor"/> row: may watch and supervise, never edit or grade.</summary>
    Proctor = 1,
    /// <summary>Teacher of the exam's section.</summary>
    Owner = 2,
    /// <summary>Admin / SuperAdmin: every exam.</summary>
    Admin = 3
}

/// <summary>
/// Module 7 / 10 — the single authority for "may this staff user supervise exam X". Used by the exam
/// read endpoints, the proctor-assignment endpoints and the monitoring hub, so an invigilator sees
/// only the exams they were assigned to (privacy NFR) and the hub never trusts a client-chosen exam id.
/// </summary>
public class ExamAccessService
{
    // Roles that can be assigned as proctors (Admins already see everything).
    public static readonly string[] ProctorRoles = ["Invigilator", "Teacher"];

    private readonly AppDbContext _db;
    public ExamAccessService(AppDbContext db) => _db = db;

    public static bool IsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin") || user.IsInRole("SuperAdmin");

    public static Guid? UserIdOf(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>Access level for <paramref name="examId"/>; <see cref="ExamAccess.None"/> when the exam does not exist.</summary>
    public async Task<ExamAccess> GetAsync(ClaimsPrincipal user, Guid examId) =>
        await FindAsync(user, examId) ?? ExamAccess.None;

    /// <summary>
    /// Like <see cref="GetAsync"/>, but <c>null</c> when the exam does not exist, so an endpoint can answer an unknown
    /// exam with 404 and a known exam the caller may not touch with 403 (D-39: one rule for every per-exam endpoint).
    /// </summary>
    public async Task<ExamAccess?> FindAsync(ClaimsPrincipal user, Guid examId)
    {
        var exam = await _db.Exams.AsNoTracking()
            .Where(e => e.ExamId == examId)
            .Select(e => new { e.Section.TeacherId })
            .FirstOrDefaultAsync();
        if (exam is null) return null;

        var userId = UserIdOf(user);
        if (userId is null) return ExamAccess.None;
        var teacherId = exam.TeacherId;

        if (IsAdmin(user)) return ExamAccess.Admin;
        if (user.IsInRole("Teacher") && teacherId == userId) return ExamAccess.Owner;
        if (ProctorRoles.Any(user.IsInRole) &&
            await _db.ExamProctors.AnyAsync(p => p.ExamId == examId && p.UserId == userId))
            return ExamAccess.Proctor;

        return ExamAccess.None;
    }

    /// <summary>Access to the exam an exam session belongs to (None for an unknown session).</summary>
    public async Task<ExamAccess> GetForSessionAsync(ClaimsPrincipal user, Guid sessionId)
    {
        var examId = await _db.ExamSessions.AsNoTracking()
            .Where(s => s.SessionId == sessionId)
            .Select(s => (Guid?)s.ExamId)
            .FirstOrDefaultAsync();
        return examId is null ? ExamAccess.None : await GetAsync(user, examId.Value);
    }

    /// <summary>
    /// The usual answer for a per-exam endpoint: 404 for an unknown exam, 403 below <paramref name="required"/>, null when
    /// the caller may go ahead. Endpoints check this before they change or audit anything.
    /// </summary>
    public async Task<IActionResult?> DenyUnlessAsync(ClaimsPrincipal user, Guid examId, ExamAccess required,
        string forbiddenMessage = "You do not have access to this exam.")
    {
        var access = await FindAsync(user, examId);
        if (access is null)
            return new NotFoundObjectResult(new { message = "Exam not found." });
        return access < required
            ? new ObjectResult(new { message = forbiddenMessage }) { StatusCode = StatusCodes.Status403Forbidden }
            : null;
    }

    /// <summary>Ids of the exams an invigilator has been assigned to (the only exams they may list).</summary>
    public IQueryable<Guid> ProctoredExamIds(Guid userId) =>
        _db.ExamProctors.Where(p => p.UserId == userId).Select(p => p.ExamId);
}
