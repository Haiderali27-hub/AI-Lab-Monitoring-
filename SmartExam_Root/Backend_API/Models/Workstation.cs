namespace Backend_API.Models;

public class Workstation
{
    public Guid WorkstationId { get; set; } = Guid.NewGuid();
    public Guid LabId { get; set; }
    public string MachineNumber { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;

    // Navigation
    public Lab Lab { get; set; } = null!;
    public ICollection<ExamAssignment> ExamAssignments { get; set; } = new List<ExamAssignment>();
}
