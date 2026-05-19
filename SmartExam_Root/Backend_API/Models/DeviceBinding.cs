namespace Backend_API.Models;

public class DeviceBinding
{
    public Guid BindingId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string HwidHash { get; set; } = string.Empty;
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
