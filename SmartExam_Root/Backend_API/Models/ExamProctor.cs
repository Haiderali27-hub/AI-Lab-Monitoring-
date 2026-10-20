namespace Backend_API.Models;

/// <summary>
/// Module 7 / 10 — a staff member assigned to supervise one exam. This row is what lets an
/// Invigilator see the exam at all (exam list, detail, readiness, live-monitor hub group), and
/// what lets a Teacher co-proctor an exam of a section they do not teach.
/// Composite key (ExamId, UserId): a person is assigned to an exam at most once.
/// </summary>
public class ExamProctor
{
    public Guid ExamId { get; set; }
    public Guid UserId { get; set; }

    // Who made the assignment. Kept as a plain id (no FK) so the row — like an audit record —
    // survives the assigning teacher's account being removed later.
    public Guid AssignedBy { get; set; }
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User User { get; set; } = null!;
}
