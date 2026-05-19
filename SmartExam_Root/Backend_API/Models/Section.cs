namespace Backend_API.Models;

public class Section
{
    public Guid SectionId { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public Guid TeacherId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Semester { get; set; } = string.Empty;

    // Navigation
    public Course Course { get; set; } = null!;
    public User Teacher { get; set; } = null!;
    public ICollection<SectionEnrollment> Enrollments { get; set; } = new List<SectionEnrollment>();
    public ICollection<Exam> Exams { get; set; } = new List<Exam>();
}
