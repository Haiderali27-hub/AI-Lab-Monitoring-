using Backend_API.Models.Enums;

namespace Backend_API.Models;

public class User
{
    public Guid UserId { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    // Module 1: unique university roll number (stored trimmed + upper-case); students may log in with it.
    public string? RollNumber { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string Salt { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public DeviceBinding? DeviceBinding { get; set; }
    public ICollection<UserSession> Sessions { get; set; } = new List<UserSession>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<ExamAssignment> ExamAssignments { get; set; } = new List<ExamAssignment>();
    public ICollection<ExamSession> ExamSessions { get; set; } = new List<ExamSession>();
}
