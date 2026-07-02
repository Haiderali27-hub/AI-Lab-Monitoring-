namespace Backend_API.Models;

public class Course
{
    public Guid CourseId { get; set; } = Guid.NewGuid();
    public Guid DeptId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;

    // Navigation
    public Department Department { get; set; } = null!;
    public ICollection<Section> Sections { get; set; } = new List<Section>();
}
