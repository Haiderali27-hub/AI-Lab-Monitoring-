namespace Backend_API.Models;

public class Department
{
    public Guid DeptId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    // Navigation
    public ICollection<Course> Courses { get; set; } = new List<Course>();
}
