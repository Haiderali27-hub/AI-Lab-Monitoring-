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
    BackupTampered,

    // Module 5 — liveness, recorded by the SERVER (heartbeat watchdog / next heartbeat). Not violations.
    Disconnected,
    Reconnected,
    // Reported by the client after a network outage: { offlineFromUtc, offlineToUtc, queuedEvents }. Not a violation.
    NetworkOffline,

    // Module 5 — on-device webcam monitoring (only when the exam requires a webcam). Debounced on the client
    // (Core/Monitoring/CameraTamperDetector): one event per episode, not per frame.
    CameraCovered,      // lens covered / image black or featureless for a few seconds
    CameraFrozen,       // identical frames for a while (virtual camera, looped still image)
    CameraUnavailable,  // the required camera stopped delivering frames (unplugged, taken by another app)

    // Module 5 (S8) — on-device webcam models on the desktop (YuNet faces, YOLOX-Nano phones; CPU, ~1.5 FPS), debounced
    // on the client (Core/Monitoring/VisionIssueDetector): one event per episode, payload { detail, atUtc }.
    NoFace,             // no face in front of the camera for 5 s
    MultipleFaces,      // two or more faces for 3 s
    LookingAway,        // head turned beyond yaw ±30° / pitch +35° −20° (relative to the student's own baseline) for 3 s
    PhoneDetected,      // a mobile phone in two consecutive phone checks

    // Module 10 — reported by the client when a proctor's unlock (hub UnlockSession) suspended its lockdown guards,
    // and when they came back on. Not violations; the unlock itself is audited on the server (SESSION_UNLOCKED).
    LockdownSuspended,
    LockdownResumed
}
