namespace Backend_API.Models;

public class Workstation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LabId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Lab Lab { get; set; } = null!;
    public ICollection<ExamAssignment> ExamAssignments { get; set; } = new List<ExamAssignment>();
}
