namespace Backend_API.Models.Enums;

/// How strictly the student client locks the workstation during an exam.
public enum LockdownMode
{
    Off,        // no lockdown (practice / development)
    Report,     // keys + window locked, unauthorized apps reported
    Enforce     // as Report, plus unauthorized apps are terminated
}
