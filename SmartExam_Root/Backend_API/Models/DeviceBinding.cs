namespace Backend_API.Models;

public class DeviceBinding
{
    public Guid BindingId { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string HwidHash { get; set; } = string.Empty;
    // 1 = legacy CPU|BOARD|MAC fingerprint, 2 = "v2|BIOS|DISK|BOARD" (thesis §device binding).
    // A v1 binding is upgraded once, on the first login that proves the old fingerprint too.
    public int HwidVersion { get; set; } = 1;
    public string? MachineName { get; set; }       // Windows computer name, e.g. LAB-PC-07
    public string? WindowsUsername { get; set; }   // Windows account logged into that PC
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public User User { get; set; } = null!;
}
