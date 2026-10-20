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
    LockdownDegraded
}
