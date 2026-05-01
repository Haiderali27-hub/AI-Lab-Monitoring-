namespace Backend_API.Models;

public class DeviceBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentUserId { get; set; }
    public string HwidHash { get; set; } = string.Empty;
    public DateTime BoundAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAtUtc { get; set; } = DateTime.UtcNow;

    public User StudentUser { get; set; } = null!;
}