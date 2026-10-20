using System.Collections.Concurrent;
using Backend_API.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend_API.Services;

/// <summary>
/// Module 7 / 10 — remembers which live connections joined which exam's monitoring group
/// (<c>exam:{examId}</c>). The hub only checks access when a proctor joins, so without this an
/// invigilator who is unassigned (or a staff account that is deactivated, deleted or given another
/// role) would keep receiving that exam's heartbeats and violations until their page reconnected.
/// <see cref="RevokeAsync"/> takes those connections out of the group at once and tells the page why.
/// In-process only, which matches the single-instance SignalR setup (no backplane).
/// </summary>
public class ExamGroupTracker
{
    /// <summary>Client method sent to a connection whose access to an exam was withdrawn.</summary>
    public const string RevokedMethod = "ExamAccessRevoked";

    private readonly IHubContext<MonitoringHub> _hub;
    // connectionId → (user, exams that connection joined)
    private readonly ConcurrentDictionary<string, Membership> _connections = new();

    private sealed record Membership(Guid UserId, ConcurrentDictionary<Guid, byte> Exams);

    public ExamGroupTracker(IHubContext<MonitoringHub> hub) => _hub = hub;

    public static string GroupName(Guid examId) => $"exam:{examId}";

    public void Joined(string connectionId, Guid userId, Guid examId) =>
        _connections.GetOrAdd(connectionId, _ => new Membership(userId, new ConcurrentDictionary<Guid, byte>()))
            .Exams[examId] = 0;

    public void Left(string connectionId, Guid examId)
    {
        if (_connections.TryGetValue(connectionId, out var m)) m.Exams.TryRemove(examId, out _);
    }

    public void Disconnected(string connectionId) => _connections.TryRemove(connectionId, out _);

    /// <summary>Exam groups <paramref name="userId"/> currently has open connections in (diagnostics / tests).</summary>
    public IReadOnlyList<(string ConnectionId, Guid ExamId)> MembershipsOf(Guid userId) =>
        _connections
            .Where(kv => kv.Value.UserId == userId)
            .SelectMany(kv => kv.Value.Exams.Keys.Select(examId => (kv.Key, examId)))
            .ToList();

    /// <summary>
    /// Removes every live connection of <paramref name="userId"/> from the group of
    /// <paramref name="examId"/> (from every exam group when null) and notifies those connections.
    /// Returns how many group memberships were removed.
    /// </summary>
    public async Task<int> RevokeAsync(Guid userId, Guid? examId = null)
    {
        var removed = new List<(string ConnectionId, Guid ExamId)>();
        foreach (var (connectionId, membership) in _connections)
        {
            if (membership.UserId != userId) continue;
            var exams = examId is { } only ? new[] { only } : membership.Exams.Keys.ToArray();
            foreach (var e in exams)
                if (membership.Exams.TryRemove(e, out _)) removed.Add((connectionId, e));
        }

        foreach (var (connectionId, e) in removed)
        {
            await _hub.Groups.RemoveFromGroupAsync(connectionId, GroupName(e));
            await _hub.Clients.Client(connectionId).SendAsync(RevokedMethod, new { examId = e });
        }
        return removed.Count;
    }
}
