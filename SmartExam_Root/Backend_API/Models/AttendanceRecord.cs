namespace Backend_API.Models;

/// <summary>
/// Module 9 — a student's class attendance in one section, imported from the teacher's CSV.
/// Eligibility is recalculated from these rows (attended / total ≥ Exam.MinAttendancePercent).
/// One row per (section, student); a re-import updates it in place.
/// </summary>
public class AttendanceRecord
{
    public Guid AttendanceId { get; set; } = Guid.NewGuid();
    public Guid SectionId { get; set; }
    public Guid UserId { get; set; }
    public int TotalClasses { get; set; }
    public int AttendedClasses { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ImportedBy { get; set; }

    // Navigation
    public Section Section { get; set; } = null!;
    public User Student { get; set; } = null!;
}
