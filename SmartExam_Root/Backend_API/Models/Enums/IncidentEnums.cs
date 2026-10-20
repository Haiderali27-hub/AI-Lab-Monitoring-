namespace Backend_API.Models.Enums;

// Module 10 incidents. Both are stored as strings (AppDbContext), so members can be added without a migration.

/// <summary>Open → Escalated → Resolved, or Open → Resolved. Resolved is final.</summary>
public enum IncidentStatus
{
    Open,
    Escalated,
    Resolved
}

public enum IncidentSeverity
{
    Low,
    Medium,
    High,
    Critical
}
