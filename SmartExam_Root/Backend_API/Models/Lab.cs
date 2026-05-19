namespace Backend_API.Models;

public class Lab
{
    public Guid LabId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Workstation> Workstations { get; set; } = new List<Workstation>();
}
