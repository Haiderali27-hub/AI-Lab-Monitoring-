namespace Backend_API.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? InstitutionId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public SystemRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Institution? Institution { get; set; }
    public ICollection<DeviceBinding> DeviceBindings { get; set; } = new List<DeviceBinding>();
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<ExamAssignment> ExamAssignments { get; set; } = new List<ExamAssignment>();
    public ICollection<ExamSession> ExamSessions { get; set; } = new List<ExamSession>();
    public ICollection<Exam> ProctoredExams { get; set; } = new List<Exam>();
    public ICollection<MonitoringEvent> MonitoringEvents { get; set; } = new List<MonitoringEvent>();
    public ICollection<SectionEnrollment> SectionEnrollments { get; set; } = new List<SectionEnrollment>();
    public ICollection<TeacherSectionAssignment> TeacherSectionAssignments { get; set; } = new List<TeacherSectionAssignment>();
}
