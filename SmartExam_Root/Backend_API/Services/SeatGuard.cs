using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Backend_API.Hubs;
using Backend_API.Models;
using Microsoft.AspNetCore.SignalR;

namespace Backend_API.Services;

/// <summary>How a request is matched to its assigned workstation (top-level config key <c>SeatMatch</c>).</summary>
public enum SeatMatchMode
{
    MachineName,   // desktop client's Windows computer name == Workstation.HostName ?? MachineNumber (default)
    Ip,            // request IP == Workstation.IpAddress
    Either         // either of the above
}

public enum SeatStatus
{
    Ok,            // assigned seat, and the caller is sitting at it
    Mismatch,      // assigned seat, caller is somewhere else
    Unassigned,    // no seat on this assignment — nothing to enforce
    NotEnforced,   // seat assigned but Exam.EnforceSeat is off
    NotChecked     // caller is not the desktop client (web token) — it carries no machine identity
}

public sealed record SeatCheckResult(
    SeatStatus Status,
    string? ExpectedSeat,
    string? ExpectedHost,
    string? ExpectedIp,
    string? ActualMachine,
    string? ActualIp)
{
    public bool IsMismatch => Status == SeatStatus.Mismatch;

    /// "logged in from PC-02 (assigned PC-01)" — shown on the live monitor tile.
    public string Detail =>
        $"logged in from {Describe(ActualMachine, ActualIp)} (assigned {ExpectedSeat ?? "?"})";

    /// The message the student sees when start is refused.
    public string StudentMessage =>
        $"This exam must be taken from seat {ExpectedSeat} (this computer: {Describe(ActualMachine, ActualIp)}). Ask the invigilator to move you or reassign your seat.";

    private static string Describe(string? machine, string? ip) =>
        string.IsNullOrWhiteSpace(machine) ? ip ?? "unknown" : string.IsNullOrWhiteSpace(ip) ? machine : $"{machine}, {ip}";
}

/// <summary>
/// Module 9 — seat enforcement shared by start-session, student/start and the heartbeat.
/// The desktop client's machine name comes from the <c>machine</c> claim stamped into its token at
/// student-login (the same value saved to DeviceBinding.MachineName); the IP is taken from the
/// request itself at check time, never from the client. Only tokens issued by student-login
/// (<c>client=desktop</c>) are seat-checked — web-portal tokens carry no machine identity; set
/// <c>Exams:RequireDesktopClientForStart=true</c> to refuse exam starts from them altogether.
/// Limitation (thesis): the machine name is client-reported; HWID binds the student, not the seat.
/// </summary>
public class SeatGuard
{
    public const string ClientClaim = "client";
    public const string DesktopClient = "desktop";
    public const string MachineClaim = "machine";
    public const string MismatchCode = "SEAT_MISMATCH";

    private readonly IConfiguration _config;
    private readonly AuditService _audit;
    private readonly IHubContext<MonitoringHub> _hub;

    public SeatGuard(IConfiguration config, AuditService audit, IHubContext<MonitoringHub> hub)
    {
        _config = config;
        _audit = audit;
        _hub = hub;
    }

    public SeatMatchMode Mode =>
        Enum.TryParse<SeatMatchMode>(_config["SeatMatch"], ignoreCase: true, out var mode) ? mode : SeatMatchMode.MachineName;

    /// Exams:RequireDesktopClientForStart — refuse exam starts from tokens that did not come from the desktop client.
    public bool DesktopClientRequired => _config.GetValue("Exams:RequireDesktopClientForStart", false);

    public static bool IsDesktopClient(ClaimsPrincipal user) =>
        string.Equals(user.FindFirst(ClientClaim)?.Value, DesktopClient, StringComparison.Ordinal);

    /// <summary>Checks the caller against the assignment's seat. Requires <c>a.Exam</c> and <c>a.Workstation</c> loaded.</summary>
    public SeatCheckResult Check(ExamAssignment a, ClaimsPrincipal user, HttpContext http)
    {
        var ws = a.Workstation;
        var machine = IsDesktopClient(user) ? user.FindFirst(MachineClaim)?.Value : null;
        var ip = NormalizeIp(http.Connection.RemoteIpAddress);

        if (a.WorkstationId is null || ws is null)
            return new SeatCheckResult(SeatStatus.Unassigned, null, null, null, machine, ip);

        SeatCheckResult Result(SeatStatus s) => new(s, ws.MachineNumber, ws.HostName, ws.IpAddress, machine, ip);

        if (!a.Exam.EnforceSeat) return Result(SeatStatus.NotEnforced);
        if (!IsDesktopClient(user)) return Result(SeatStatus.NotChecked);

        return Result(Matches(Mode, ws.HostName, ws.MachineNumber, ws.IpAddress, machine, ip)
            ? SeatStatus.Ok
            : SeatStatus.Mismatch);
    }

    /// <summary>Pure matching rule (unit-tested). Names compare trimmed and case-insensitively.</summary>
    public static bool Matches(SeatMatchMode mode, string? hostName, string machineNumber, string? workstationIp,
        string? actualMachine, string? actualIp)
    {
        var expectedName = string.IsNullOrWhiteSpace(hostName) ? machineNumber : hostName;
        var byName = !string.IsNullOrWhiteSpace(actualMachine) && !string.IsNullOrWhiteSpace(expectedName) &&
                     string.Equals(expectedName.Trim(), actualMachine.Trim(), StringComparison.OrdinalIgnoreCase);
        var byIp = IpEquals(workstationIp, actualIp);

        return mode switch
        {
            SeatMatchMode.Ip => byIp,
            SeatMatchMode.Either => byName || byIp,
            _ => byName
        };
    }

    public static string? NormalizeIp(IPAddress? address)
    {
        if (address is null) return null;
        return (address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address).ToString();
    }

    private static bool IpEquals(string? expected, string? actual)
    {
        if (!IPAddress.TryParse(expected?.Trim(), out var a) || !IPAddress.TryParse(actual?.Trim(), out var b)) return false;
        return NormalizeIp(a) == NormalizeIp(b);
    }

    /// <summary>
    /// Audits SEAT_MISMATCH (staged — the caller's SaveChanges persists it) and raises a live-monitor
    /// alert to the exam's proctors. <paramref name="phase"/> is "start" or "heartbeat".
    /// </summary>
    public async Task ReportMismatchAsync(Guid examId, Guid userId, Guid? sessionId, SeatCheckResult r, string phase)
    {
        _audit.Add(userId, MismatchCode, "Exam", examId.ToString(), new
        {
            examId, studentId = userId, sessionId, phase, mode = Mode.ToString(),
            expectedSeat = r.ExpectedSeat, expectedHost = r.ExpectedHost, expectedIp = r.ExpectedIp,
            actualMachine = r.ActualMachine, actualIp = r.ActualIp
        });

        await _hub.Clients.Group($"exam:{examId}").SendAsync("ViolationEvent", new
        {
            userId,
            eventType = MismatchCode,
            violationType = "SeatMismatch",
            payload = MismatchPayload(r, phase)
        });
    }

    /// JSON stored on the SeatMismatch monitoring event and sent with the live alert.
    public static string MismatchPayload(SeatCheckResult r, string phase) => JsonSerializer.Serialize(new
    {
        reason = "SeatMismatch",
        detail = r.Detail,
        phase,
        expectedSeat = r.ExpectedSeat,
        actualMachine = r.ActualMachine,
        actualIp = r.ActualIp
    });
}
