using Backend_API.Data;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Services.Monitoring;

/// <summary>
/// Module 5 — privacy retention for capture evidence. Every <c>Storage:CleanupIntervalSeconds</c> (default 3600;
/// 0 or less disables the job) this deletes screenshots captured more than <c>Storage:RetentionDays</c> (default 30)
/// ago: the file first (a file that is already gone is fine), then the row. Files older than the window that no
/// row refers to any more (a deleted session, an upload that never committed) are removed too.
/// </summary>
public sealed class EvidenceRetentionService(
    IServiceScopeFactory scopes,
    EvidenceStorage storage,
    IConfiguration configuration,
    ILogger<EvidenceRetentionService> logger) : BackgroundService
{
    public const string RetentionDaysKey = "Storage:RetentionDays";
    public const string IntervalSecondsKey = "Storage:CleanupIntervalSeconds";
    public const int DefaultRetentionDays = 30;
    public const int DefaultIntervalSeconds = 3600;
    private const int BatchSize = 500;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue(IntervalSecondsKey, DefaultIntervalSeconds);
        var days = Math.Max(1, configuration.GetValue(RetentionDaysKey, DefaultRetentionDays));
        if (interval <= 0)
        {
            logger.LogInformation("Evidence retention job disabled ({Key} = {Seconds}).", IntervalSecondsKey, interval);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
        do
        {
            try
            {
                await SweepAsync(DateTime.UtcNow - TimeSpan.FromDays(days), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Evidence retention sweep failed.");
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

    /// <summary>Deletes everything captured before <paramref name="cutoffUtc"/>. Returns how many rows were removed.</summary>
    public async Task<int> SweepAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)
    {
        var removed = 0;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expired = await db.Screenshots
                .Where(s => s.CapturedAt < cutoffUtc)
                .OrderBy(s => s.CapturedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (expired.Count == 0) break;

            // A file that cannot be deleted right now (locked by a viewer) keeps its row for the next sweep.
            var deletable = expired.Where(s => storage.Delete(s.StoragePath)).ToList();
            if (deletable.Count == 0) break;
            db.Screenshots.RemoveRange(deletable);
            await db.SaveChangesAsync(cancellationToken);
            removed += deletable.Count;
            if (expired.Count < BatchSize) break;
        }

        var referenced = (await db.Screenshots.AsNoTracking().Select(s => s.StoragePath).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = storage.DeleteOrphansOlderThan(cutoffUtc, referenced);

        if (removed > 0 || orphans > 0)
            logger.LogInformation("Evidence retention removed {Rows} expired capture(s) and {Orphans} orphaned file(s).", removed, orphans);
        return removed;
    }
}
