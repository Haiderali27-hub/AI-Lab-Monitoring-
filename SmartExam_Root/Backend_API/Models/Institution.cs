namespace Backend_API.Models;

public class Institution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? AllowedIpRanges { get; set; } // Comma-separated CIDR or IP ranges
    
    public bool EnforceSingleDeviceBinding { get; set; } = true;
    public bool AllowTeacherResetBinding { get; set; } = false;
    public int SessionTimeoutMinutes { get; set; } = 30;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Exam> Exams { get; set; } = new List<Exam>();
    public ICollection<Lab> Labs { get; set; } = new List<Lab>();
    public ICollection<Department> Departments { get; set; } = new List<Department>();
    public ICollection<Course> Courses { get; set; } = new List<Course>();
    public ICollection<Section> Sections { get; set; } = new List<Section>();
}
