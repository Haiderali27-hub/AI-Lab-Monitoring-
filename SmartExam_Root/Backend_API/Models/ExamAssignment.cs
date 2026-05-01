namespace Backend_API.Models;

public class ExamAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid StudentUserId { get; set; }
    public Guid? WorkstationId { get; set; }
    public bool IsEligible { get; set; } = true;
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

    public Exam Exam { get; set; } = null!;
    public User StudentUser { get; set; } = null!;
    public Workstation? Workstation { get; set; }
}
