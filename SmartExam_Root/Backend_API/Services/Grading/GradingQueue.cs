using System.Collections.Concurrent;
using System.Threading.Channels;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend_API.Services.Grading;

/// <summary>
/// Module 11 — answers waiting for the background grader (ids only; the database row with Status=Pending is the source
/// of truth, so nothing is lost on a restart). An answer already waiting is not queued twice.
/// </summary>
public sealed class GradingQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = false });
    private readonly ConcurrentDictionary<Guid, byte> _waiting = new();

    public ChannelReader<Guid> Reader => _channel.Reader;

    public int Waiting => _waiting.Count;

    public void Enqueue(Guid answerId)
    {
        if (_waiting.TryAdd(answerId, 0)) _channel.Writer.TryWrite(answerId);
    }

    /// <summary>Called when a worker takes the answer: a regrade requested from now on queues it again.</summary>
    public void MarkStarted(Guid answerId) => _waiting.TryRemove(answerId, out _);
}

/// <summary>
/// Module 11 — drains <see cref="GradingQueue"/> with <c>Grading:MaxParallel</c> consumers (default 2), each answer in its
/// own DI scope through <see cref="GradingService.ProcessAsync"/>. On start it re-queues every row still Pending, so a
/// restart in the middle of an exam loses no grading. Submit never waits for any of this.
/// </summary>
public sealed class GradingWorker(
    GradingQueue queue,
    IServiceScopeFactory scopes,
    IOptions<GradingOptions> options,
    ILogger<GradingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RequeuePendingAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Database briefly unreachable at start-up: new submissions are still graded.
            logger.LogError(ex, "Could not re-queue pending grading jobs.");
        }

        var consumers = Enumerable.Range(0, Math.Clamp(options.Value.MaxParallel, 1, 16))
            .Select(_ => ConsumeAsync(stoppingToken));
        await Task.WhenAll(consumers);
    }

    private async Task RequeuePendingAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.AiGradingResults.AsNoTracking()
            .Where(g => g.Status == AiGradingResult.StatusPending)
            .OrderBy(g => g.GradedAt)
            .Select(g => g.AnswerId)
            .ToListAsync(cancellationToken);
        foreach (var id in pending) queue.Enqueue(id);
        if (pending.Count > 0) logger.LogInformation("Re-queued {Count} pending grading job(s).", pending.Count);
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var answerId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                queue.MarkStarted(answerId);
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<GradingService>().ProcessAsync(answerId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;     // shutting down: the row stays Pending and is picked up on the next start
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Grading answer {AnswerId} failed.", answerId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
