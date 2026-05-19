namespace Backend_API.Models;

public class SectionEnrollment
{
    public Guid EnrollmentId { get; set; } = Guid.NewGuid();
    public Guid SectionId { get; set; }
    public Guid UserId { get; set; }

    // Navigation
    public Section Section { get; set; } = null!;
    public User Student { get; set; } = null!;
}
