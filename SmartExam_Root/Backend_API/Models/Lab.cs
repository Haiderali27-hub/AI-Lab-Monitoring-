namespace Backend_API.Models;

public class Lab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InstitutionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int RegisteredTerminals { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Institution Institution { get; set; } = null!;
}
