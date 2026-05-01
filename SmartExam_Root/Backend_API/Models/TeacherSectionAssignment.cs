namespace Backend_API.Models;

public class TeacherSectionAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SectionId { get; set; }
    public Guid TeacherUserId { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;

    public Section Section { get; set; } = null!;
    public User TeacherUser { get; set; } = null!;
}
