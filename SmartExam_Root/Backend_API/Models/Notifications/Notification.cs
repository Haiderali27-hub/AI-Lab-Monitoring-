namespace Backend_API.Models.Notifications;

// Stores every notification sent (in-app + email)
public class Notification
{
    public Guid NotificationId { get; set; } = Guid.NewGuid();
    public Guid RecipientId { get; set; }           // Who receives it
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "ExamScheduled" | "GradeReleased" | "ViolationWarning" | "EligibilityChanged" | "ExamReminder"
    public bool IsRead { get; set; } = false;
    public bool EmailSent { get; set; } = false;
    public string? RelatedEntityId { get; set; }     // examId or sessionId this notification is about
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User Recipient { get; set; } = null!;
}
