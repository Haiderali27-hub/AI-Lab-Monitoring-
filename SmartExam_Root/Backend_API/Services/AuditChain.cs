using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Backend_API.Data;
using Backend_API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Backend_API.Services;

/// <summary>
/// Module 12 — the audit log as a tamper-evident hash chain.
/// <para>Every row gets a gap-free <c>Sequence</c> (1, 2, 3, …), the previous row's hash as <c>PrevHash</c> (64 zeros for
/// row 1) and <c>Hash = SHA-256(PrevHash ‖ Sequence ‖ LogId ‖ ActorId ‖ EventType ‖ EntityType ‖ EntityId ‖
/// canonical(Details) ‖ CreatedAt)</c>, the fields framed as one JSON array so no two field sets encode alike.
/// Changing any field of a row breaks its own hash; deleting a row leaves a sequence gap at the next one; re-hashing
/// a forged row breaks the next row's <c>PrevHash</c>. Truncating the newest rows is the one change a chain alone cannot
/// show — the last verified hash is reported so the head can be noted outside the database.</para>
/// <para>Rows are chained when they are saved (<see cref="AppDbContext"/> calls <see cref="SaveSealedAsync"/>), one writer
/// at a time: a process-wide lock, plus on PostgreSQL a transaction-scoped advisory lock held until commit, so two
/// requests (or two server instances) can never take the same sequence number.</para>
/// </summary>
public static class AuditChain
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";
    public const string BackfillEventType = "AUDIT_CHAIN_BACKFILLED";

    // Transaction-scoped PostgreSQL advisory lock; the key is an arbitrary constant reserved for the audit writer.
    private const string AdvisoryLockSql = "SELECT pg_advisory_xact_lock(530120012)";
    private const int VerifyPageSize = 2000;
    private const string NpgsqlProvider = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static readonly SemaphoreSlim WriterLock = new(1, 1);

    public sealed record ChainStatus(
        bool Valid, long? BrokenAt, string? Reason, long CheckedCount, int Unsealed, long? LastVerifiedSequence, string? LastVerifiedHash);

    // ── Hashing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The stored form of a timestamp: UTC, cut to whole microseconds (PostgreSQL keeps no more), so the value hashed
    /// is the value read back.
    /// </summary>
    public static DateTime NormalizeTimestamp(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Local => value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
            _ => value
        };
        return new DateTime(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }

    /// <summary>
    /// Canonical JSON: object keys sorted (ordinal), no whitespace, so the same details hash the same whatever the key
    /// order. A value that is not valid JSON is hashed as it is (canonical output is always valid JSON, so the two
    /// cannot collide).
    /// </summary>
    public static string CanonicalJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return json ?? string.Empty;
        try
        {
            var node = JsonNode.Parse(json);
            return node is null ? "null" : Canonical(node).ToJsonString();
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return json;
        }
    }

    private static JsonNode Canonical(JsonNode node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, p.Value is null ? null : Canonical(p.Value)))),
        JsonArray arr => new JsonArray(arr.Select(v => v is null ? null : Canonical(v)).ToArray()),
        _ => node.DeepClone()
    };

    public static string ComputeHash(AuditLog row, long sequence, string prevHash)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartArray();
            w.WriteStringValue(prevHash);
            w.WriteNumberValue(sequence);
            w.WriteStringValue(row.LogId.ToString("D"));
            if (row.ActorId is Guid actor) w.WriteStringValue(actor.ToString("D")); else w.WriteNullValue();
            w.WriteStringValue(row.EventType);
            w.WriteStringValue(row.EntityType);
            if (row.EntityId is null) w.WriteNullValue(); else w.WriteStringValue(row.EntityId);
            w.WriteStringValue(CanonicalJson(row.Details));
            // Microseconds since 0001-01-01 UTC: exact, and independent of DateTime.Kind and culture.
            w.WriteNumberValue(NormalizeTimestamp(row.CreatedAt).Ticks / 10);
            w.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    /// <summary>Links <paramref name="rows"/> (in the given order) onto the chain head.</summary>
    public static void Seal(IEnumerable<AuditLog> rows, long? headSequence, string? headHash)
    {
        var sequence = headSequence ?? 0;
        var prev = headSequence is null ? GenesisHash : headHash ?? GenesisHash;
        foreach (var row in rows)
        {
            row.CreatedAt = NormalizeTimestamp(row.CreatedAt);
            row.Sequence = ++sequence;
            row.PrevHash = prev;
            row.Hash = ComputeHash(row, sequence, prev);
            prev = row.Hash;
        }
    }

    // ── Serialized writer ────────────────────────────────────────────────────

    /// <summary>
    /// Saves <paramref name="db"/>'s pending changes with its new audit rows chained, holding the writer lock from
    /// reading the chain head until the rows are committed.
    /// </summary>
    internal static async Task<int> SaveSealedAsync(DbContext db, IReadOnlyList<AuditLog> added, Func<Task<int>> save,
        CancellationToken cancellationToken)
    {
        await WriterLock.WaitAsync(cancellationToken);
        try
        {
            var own = await EnterDatabaseLockAsync(db, cancellationToken);
            try
            {
                var head = await HeadAsync(db, cancellationToken);
                Seal(added.OrderBy(r => r.CreatedAt), head?.Sequence, head?.Hash);
                var written = await save();
                if (own is not null) await own.CommitAsync(cancellationToken);
                return written;
            }
            finally
            {
                if (own is not null) await own.DisposeAsync();
            }
        }
        finally
        {
            WriterLock.Release();
        }
    }

    /// <summary>Synchronous twin of <see cref="SaveSealedAsync"/> for <c>SaveChanges()</c>.</summary>
    internal static int SaveSealed(DbContext db, IReadOnlyList<AuditLog> added, Func<int> save)
    {
        WriterLock.Wait();
        try
        {
            IDbContextTransaction? own = null;
            if (db.Database.IsRelational())
            {
                if (db.Database.CurrentTransaction is null) own = db.Database.BeginTransaction();
                if (db.Database.ProviderName == NpgsqlProvider)
                    db.Database.ExecuteSqlRaw(AdvisoryLockSql);
            }
            try
            {
                var head = db.Set<AuditLog>().AsNoTracking()
                    .Where(a => a.Sequence != null)
                    .OrderByDescending(a => a.Sequence)
                    .Select(a => new { a.Sequence, a.Hash })
                    .FirstOrDefault();
                Seal(added.OrderBy(r => r.CreatedAt), head?.Sequence, head?.Hash);
                var written = save();
                own?.Commit();
                return written;
            }
            finally
            {
                own?.Dispose();
            }
        }
        finally
        {
            WriterLock.Release();
        }
    }

    // Opens a transaction when the caller has none (relational providers only) and, on PostgreSQL, takes the
    // advisory lock inside it — released by the commit, so a second instance cannot read the head before then.
    private static async Task<IDbContextTransaction?> EnterDatabaseLockAsync(DbContext db, CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational()) return null;
        var own = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        if (db.Database.ProviderName == NpgsqlProvider)
            await db.Database.ExecuteSqlRawAsync(AdvisoryLockSql, cancellationToken);
        return own;
    }

    private sealed record Head(long? Sequence, string? Hash);

    private static Task<Head?> HeadAsync(DbContext db, CancellationToken cancellationToken) =>
        db.Set<AuditLog>().AsNoTracking()
            .Where(a => a.Sequence != null)
            .OrderByDescending(a => a.Sequence)
            .Select(a => new Head(a.Sequence, a.Hash))
            .FirstOrDefaultAsync(cancellationToken);

    // ── Backfill and verification ────────────────────────────────────────────

    /// <summary>
    /// Seals the rows written before the chain existed (no <c>Sequence</c>), oldest first, onto the end of the chain,
    /// and records the backfill itself as the next row. Returns how many legacy rows were sealed (0 = nothing to do,
    /// and nothing is written).
    /// </summary>
    public static async Task<int> BackfillAsync(AppDbContext db, Guid? actorId, CancellationToken cancellationToken = default)
    {
        await WriterLock.WaitAsync(cancellationToken);
        try
        {
            var own = await EnterDatabaseLockAsync(db, cancellationToken);
            try
            {
                var legacy = await db.AuditLogs
                    .Where(a => a.Sequence == null)
                    .OrderBy(a => a.CreatedAt).ThenBy(a => a.LogId)
                    .ToListAsync(cancellationToken);
                if (legacy.Count == 0) return 0;

                var head = await HeadAsync(db, cancellationToken);
                var marker = new AuditLog
                {
                    ActorId = actorId,
                    EventType = BackfillEventType,
                    EntityType = "AuditLog",
                    Details = JsonSerializer.Serialize(new
                    {
                        @sealed = legacy.Count,
                        oldest = legacy[0].CreatedAt,
                        newest = legacy[^1].CreatedAt,
                        firstSequence = (head?.Sequence ?? 0) + 1
                    }),
                    CreatedAt = DateTime.UtcNow
                };
                db.AuditLogs.Add(marker);
                Seal(legacy.Append(marker), head?.Sequence, head?.Hash);

                await db.SaveChangesWithoutChainingAsync(cancellationToken);
                if (own is not null) await own.CommitAsync(cancellationToken);
                return legacy.Count;
            }
            finally
            {
                if (own is not null) await own.DisposeAsync();
            }
        }
        finally
        {
            WriterLock.Release();
        }
    }

    /// <summary>
    /// Walks the chain in sequence order and reports the first broken link: a sequence gap or a row out of place
    /// (reported at the row found there), a <c>PrevHash</c> that does not match the previous row's hash, or a row whose
    /// fields no longer match its own hash. Rows still awaiting the backfill make the chain not valid either.
    /// </summary>
    public static async Task<ChainStatus> VerifyAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        long expected = 1;
        var prev = GenesisHash;
        long checkedCount = 0;
        long? lastSequence = null;
        long? brokenAt = null;
        string? reason = null;

        while (brokenAt is null)
        {
            var after = lastSequence ?? long.MinValue;
            var page = await db.AuditLogs.AsNoTracking()
                .Where(a => a.Sequence != null && a.Sequence > after)
                .OrderBy(a => a.Sequence)
                .Take(VerifyPageSize)
                .ToListAsync(cancellationToken);
            if (page.Count == 0) break;

            foreach (var row in page)
            {
                var sequence = row.Sequence!.Value;
                if (sequence != expected)
                {
                    (brokenAt, reason) = (sequence, sequence > expected
                        ? $"Sequence gap: expected #{expected}, found #{sequence} (row missing)."
                        : $"Duplicate sequence #{sequence}.");
                    break;
                }
                if (!string.Equals(row.PrevHash, prev, StringComparison.OrdinalIgnoreCase))
                {
                    (brokenAt, reason) = (sequence, $"Row #{sequence} does not link to the previous row's hash.");
                    break;
                }
                if (!string.Equals(row.Hash, ComputeHash(row, sequence, prev), StringComparison.OrdinalIgnoreCase))
                {
                    (brokenAt, reason) = (sequence, $"Row #{sequence} was modified after it was written (hash mismatch).");
                    break;
                }
                prev = row.Hash!;
                expected++;
                checkedCount++;
            }
            lastSequence = page[^1].Sequence;
        }

        var unsealed = await db.AuditLogs.CountAsync(a => a.Sequence == null, cancellationToken);
        if (brokenAt is null && unsealed > 0)
            reason = $"{unsealed} row(s) written before the hash chain are not sealed yet (run the backfill).";

        return new ChainStatus(
            Valid: brokenAt is null && unsealed == 0,
            BrokenAt: brokenAt,
            Reason: reason,
            CheckedCount: checkedCount,
            Unsealed: unsealed,
            LastVerifiedSequence: checkedCount == 0 ? null : expected - 1,
            LastVerifiedHash: checkedCount == 0 ? null : prev);
    }
}
