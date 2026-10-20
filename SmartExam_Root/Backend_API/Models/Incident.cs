using Backend_API.Models.Enums;

namespace Backend_API.Models;

/// <summary>
/// Module 10 — something a proctor saw during an exam session and wrote down ("phone on the desk", "talking to the
/// neighbour"), as opposed to the violations the desktop client detects on its own. Created from the Live Monitor
/// drawer, escalated to the exam's teacher when it needs a decision, and resolved with a note. Every change is
/// audited (INCIDENT_CREATED / _UPDATED / _ESCALATED / _RESOLVED), so the audit log is the incident's full history;
/// the row keeps the current state for the dashboard and reports.
/// </summary>
public class Incident
{
    public const int MaxNotesLength = 2000;

    public Guid IncidentId { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public Guid ExamId { get; set; }                 // denormalised from the session: per-exam listing and badges
    public Guid CreatedBy { get; set; }              // plain id like an audit actor: survives the account's removal
    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;
    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    // What the proctor observed (editable while the incident is not resolved).
    public string Notes { get; set; } = string.Empty;
    // The note given with the latest status change (why it was escalated, how it was resolved).
    public string? StatusNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public Guid? ResolvedBy { get; set; }

    // Navigation
    public ExamSession Session { get; set; } = null!;
}
