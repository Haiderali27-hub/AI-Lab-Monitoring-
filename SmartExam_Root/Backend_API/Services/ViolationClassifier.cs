using System.Text.Json;
using Backend_API.Models.Enums;

namespace Backend_API.Services;

/// <summary>
/// Maps monitoring events to violation types and turns a session's violations into the
/// live-monitor tile status: Normal → Warning → Suspicious → Violation.
/// </summary>
public static class ViolationClassifier
{
    public const string Normal = "Normal";
    public const string Warning = "Warning";
    public const string Suspicious = "Suspicious";
    public const string Violation = "Violation";

    /// Every event type that counts as a violation (for counts and reports).
    public static readonly MonitoringEventType[] ViolationTypes =
    {
        MonitoringEventType.Violation,
        MonitoringEventType.FocusLost,
        MonitoringEventType.KeyBlocked,
        MonitoringEventType.ClipboardBlocked,
        MonitoringEventType.UnauthorizedProcess,
        MonitoringEventType.ProcessKilled,
        MonitoringEventType.UsbInserted,
        MonitoringEventType.MultiMonitor,
        MonitoringEventType.RemoteSession,
        MonitoringEventType.VirtualMachine,
        MonitoringEventType.LockdownDegraded,
        MonitoringEventType.SeatMismatch
    };

    private enum Severity { Warning = 1, Suspicious = 2, Violation = 3 }

    private static Severity SeverityOf(MonitoringEventType type) => type switch
    {
        MonitoringEventType.FocusLost or MonitoringEventType.KeyBlocked or MonitoringEventType.ClipboardBlocked
            => Severity.Warning,
        MonitoringEventType.UnauthorizedProcess or MonitoringEventType.MultiMonitor
            or MonitoringEventType.VirtualMachine or MonitoringEventType.LockdownDegraded
            => Severity.Suspicious,
        // ProcessKilled = the student actually launched a forbidden app; USB/RDP = evidence risk;
        // SeatMismatch = someone sitting at the wrong workstation (possible impersonation);
        // generic Violation keeps the pre-Module-4 behaviour (any violation shows red).
        _ => Severity.Violation
    };

    public static bool IsViolation(MonitoringEventType type) => ViolationTypes.Contains(type);

    /// <summary>
    /// Resolves the stored event type. Accepts a specific type name directly ("ProcessKilled")
    /// or the client's generic form: "Violation" with a payload { "reason": "ProcessKilled" }.
    /// Unknown names fall back to WindowFocus (non-violation) as before.
    /// </summary>
    public static MonitoringEventType Resolve(string? eventType, string? payloadJson)
    {
        if (string.Equals(eventType, "Violation", StringComparison.OrdinalIgnoreCase))
            return TryParseType(ReasonFrom(payloadJson), out var specific) ? specific : MonitoringEventType.Violation;

        return TryParseType(eventType, out var parsed) && parsed != MonitoringEventType.Heartbeat
            ? parsed
            : MonitoringEventType.WindowFocus;
    }

    /// Tile status for a session from all of its violation events.
    public static string Classify(IEnumerable<MonitoringEventType> events)
    {
        int warnings = 0, suspicious = 0;
        foreach (var type in events.Where(IsViolation))
        {
            switch (SeverityOf(type))
            {
                case Severity.Violation: return Violation;
                case Severity.Suspicious: suspicious++; break;
                default: warnings++; break;
            }
        }

        // Repeated lower-level offences escalate.
        if (suspicious >= 2) return Violation;
        if (suspicious == 1 || warnings >= 3) return Suspicious;
        return warnings > 0 ? Warning : Normal;
    }

    private static bool TryParseType(string? name, out MonitoringEventType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(name)) return false;
        // Older clients send "FocusLoss"
        if (string.Equals(name, "FocusLoss", StringComparison.OrdinalIgnoreCase)) name = nameof(MonitoringEventType.FocusLost);
        return Enum.TryParse(name, ignoreCase: true, out type) && Enum.IsDefined(type);
    }

    private static string? ReasonFrom(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("reason", out var reason) &&
                   reason.ValueKind == JsonValueKind.String
                ? reason.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
