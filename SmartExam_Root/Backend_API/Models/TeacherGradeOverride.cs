namespace Backend_API.Models;

public class TeacherGradeOverride
{
    public Guid OverrideId { get; set; } = Guid.NewGuid();
    public Guid AnswerId { get; set; }
    public Guid TeacherId { get; set; }
    public double FinalMarks { get; set; }
    public string? Note { get; set; }
    public DateTime OverriddenAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Answer Answer { get; set; } = null!;
    public User Teacher { get; set; } = null!;
}
