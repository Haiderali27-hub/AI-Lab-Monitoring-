namespace Backend_API.Models;

public class Exam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public Institution Institution { get; set; } = null!;
    public ICollection<ExamAssignment> Assignments { get; set; } = new List<ExamAssignment>();
    public ICollection<ExamSession> Sessions { get; set; } = new List<ExamSession>();
}