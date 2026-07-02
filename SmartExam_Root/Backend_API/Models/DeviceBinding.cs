namespace Backend_API.Models;

public class DeviceBinding
{
    public Guid BindingId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string HwidHash { get; set; } = string.Empty;
    public string? MachineName { get; set; }       // Windows computer name, e.g. LAB-PC-07
    public string? WindowsUsername { get; set; }   // Windows account logged into that PC
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
