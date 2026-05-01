namespace Backend_API.Models;

public class Course
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionId { get; set; }
    public Guid DepartmentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Institution Institution { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public ICollection<Section> Sections { get; set; } = new List<Section>();
}
