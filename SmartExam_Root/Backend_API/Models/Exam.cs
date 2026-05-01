namespace Backend_API.Models;

public class Exam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionId { get; set; }
    public Guid? LabId { get; set; }
    public Guid? ProctorUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsCancelled { get; set; } = false;
    public bool IsArchived { get; set; } = false;

    public Institution Institution { get; set; } = null!;
    public Lab? Lab { get; set; }
    public User? ProctorUser { get; set; }
    public ICollection<ExamAssignment> Assignments { get; set; } = new List<ExamAssignment>();
    public ICollection<ExamSession> Sessions { get; set; } = new List<ExamSession>();
}