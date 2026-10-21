using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services.Monitoring;

/// <summary>
/// Module 5 — server-side disconnect detection. The desktop client heartbeats every 10 s; a running session whose
/// last heartbeat (or, before the first one, its start) is older than <c>Monitoring:DisconnectAfterSeconds</c>
/// (default 30) is flagged <see cref="ExamSession.IsDisconnected"/>, gets a <see cref="MonitoringEventType.Disconnected"/>
/// event and is announced to the exam's proctors as <c>StudentDisconnected</c>. The flag is cleared by the next
/// heartbeat (<c>MonitoringController.Heartbeat</c>, which records <c>Reconnected</c>). Sweeps run every
/// <c>Monitoring:DisconnectSweepSeconds</c> (default 5; 0 or less disables the watchdog).
/// </summary>
public sealed class HeartbeatWatchdogService(
    IServiceScopeFactory scopes,
    IHubContext<MonitoringHub> hub,
    IConfiguration configuration,
    ILogger<HeartbeatWatchdogService> logger) : BackgroundService
{
    public const string DisconnectAfterKey = "Monitoring:DisconnectAfterSeconds";
    public const string SweepSecondsKey = "Monitoring:DisconnectSweepSeconds";
    public const int DefaultDisconnectAfterSeconds = 30;
    public const int DefaultSweepSeconds = 5;
    public const string DisconnectedAuditEvent = "SESSION_DISCONNECTED";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sweep = configuration.GetValue(SweepSecondsKey, DefaultSweepSeconds);
        var after = TimeSpan.FromSeconds(Math.Max(5, configuration.GetValue(DisconnectAfterKey, DefaultDisconnectAfterSeconds)));
        if (sweep <= 0)
        {
            logger.LogInformation("Heartbeat watchdog disabled ({Key} = {Seconds}).", SweepSecondsKey, sweep);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(sweep));
        do
        {
            try
            {
                await SweepAsync(DateTime.UtcNow, after, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A failed sweep (database briefly unreachable) must not stop future sweeps.
                logger.LogError(ex, "Heartbeat watchdog sweep failed.");
            }
        } while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Flags every running session that went silent. Returns how many were flagged.</summary>
    public async Task<int> SweepAsync(DateTime utcNow, TimeSpan disconnectAfter, CancellationToken cancellationToken = default)
    {
        var silentSince = utcNow - disconnectAfter;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var silent = await db.ExamSessions
            .Where(s => s.Status == SessionStatus.InProgress && !s.IsDisconnected &&
                        (s.LastHeartbeatAt ?? s.StartedAt) < silentSince)
            .ToListAsync(cancellationToken);
        if (silent.Count == 0) return 0;

        var audit = scope.ServiceProvider.GetRequiredService<AuditService>();
        foreach (var session in silent)
        {
            var lastSeen = session.LastHeartbeatAt ?? session.StartedAt;
            // Module 12: a network drop is part of the audit trail (the reconnect is audited by the next heartbeat).
            audit.Add(null, DisconnectedAuditEvent, "ExamSession", session.SessionId.ToString(), new
            {
                examId = session.ExamId,
                userId = session.UserId,
                lastHeartbeatAt = session.LastHeartbeatAt,
                silentSeconds = (int)(utcNow - lastSeen).TotalSeconds
            });
            session.IsDisconnected = true;
            session.DisconnectedAt = utcNow;
            db.MonitoringEvents.Add(new MonitoringEvent
            {
                EventId = Guid.NewGuid(),
                ExamSessionId = session.SessionId,
                EventType = MonitoringEventType.Disconnected,
                Payload = System.Text.Json.JsonSerializer.Serialize(new
                {
                    lastHeartbeatAt = session.LastHeartbeatAt,
                    silentSeconds = (int)(utcNow - lastSeen).TotalSeconds
                }),
                RecordedAt = utcNow,
                ReceivedAt = utcNow
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A session was finalised in the same instant (Status is a concurrency token): the next sweep re-evaluates.
            logger.LogInformation("Heartbeat watchdog: a session changed concurrently; retrying next sweep.");
            return 0;
        }

        foreach (var session in silent)
        {
            await hub.Clients.Group(ExamGroupTracker.GroupName(session.ExamId)).SendAsync("StudentDisconnected", new
            {
                userId = session.UserId,
                sessionId = session.SessionId,
                examId = session.ExamId,
                lastHeartbeatAt = session.LastHeartbeatAt,
                disconnectedAt = utcNow
            }, cancellationToken);
        }
        logger.LogInformation("Heartbeat watchdog flagged {Count} silent session(s).", silent.Count);
        return silent.Count;
    }
}
