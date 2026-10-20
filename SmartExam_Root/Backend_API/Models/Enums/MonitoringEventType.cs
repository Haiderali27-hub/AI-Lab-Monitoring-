namespace Backend_API.Models.Enums;

// Stored as a string column, so new members can be added without a migration.
public enum MonitoringEventType
{
    Heartbeat,
    WindowFocus,
    ProcessList,
    Violation,          // generic / legacy violation (no specific reason)

    // Module 4 — specific lockdown violations reported by the student client
    FocusLost,
    KeyBlocked,
    ClipboardBlocked,
    UnauthorizedProcess,
    ProcessKilled,
    UsbInserted,
    MultiMonitor,
    RemoteSession,
    VirtualMachine,
    LockdownDegraded,

    // Module 9 — recorded by the SERVER when a heartbeat comes from a seat other than the assigned one
    SeatMismatch,

    // Module 6 — the desktop app found its encrypted local answer backup altered (AES-GCM tag failure)
    // and fell back to the server's copy
    BackupTampered
}
