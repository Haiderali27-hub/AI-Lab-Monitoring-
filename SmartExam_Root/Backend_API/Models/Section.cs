namespace Backend_API.Models;

public class Section
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid CourseId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Institution Institution { get; set; } = null!;
    public Department Department { get; set; } = null!;
    public Course Course { get; set; } = null!;
    public ICollection<SectionEnrollment> Enrollments { get; set; } = new List<SectionEnrollment>();
    public ICollection<TeacherSectionAssignment> TeacherAssignments { get; set; } = new List<TeacherSectionAssignment>();
}
