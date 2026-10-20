namespace Backend_API.Models;

public class Workstation
{
    public Guid WorkstationId { get; set; } = Guid.NewGuid();
    public Guid LabId { get; set; }
    public string MachineNumber { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    // Real Windows computer name of the PC on this seat (e.g. LAB-PC-07). Seat enforcement
    // matches the desktop client's machine name against HostName, else MachineNumber.
    public string? HostName { get; set; }

    // Navigation
    public Lab Lab { get; set; } = null!;
    public ICollection<ExamAssignment> ExamAssignments { get; set; } = new List<ExamAssignment>();
}
