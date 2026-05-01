namespace Backend_API.Models;

public class SectionEnrollment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SectionId { get; set; }
    public Guid StudentUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime EnrolledAtUtc { get; set; } = DateTime.UtcNow;

    public Section Section { get; set; } = null!;
    public User StudentUser { get; set; } = null!;
}
