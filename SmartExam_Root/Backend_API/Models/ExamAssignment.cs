namespace Backend_API.Models;

public class ExamAssignment
{
    public Guid AssignmentId { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid UserId { get; set; }
    public Guid? WorkstationId { get; set; }
    public bool IsEligible { get; set; } = true;
    public string? EligibilityNote { get; set; }

    // Navigation
    public Exam Exam { get; set; } = null!;
    public User Student { get; set; } = null!;
    public Workstation? Workstation { get; set; }
}
