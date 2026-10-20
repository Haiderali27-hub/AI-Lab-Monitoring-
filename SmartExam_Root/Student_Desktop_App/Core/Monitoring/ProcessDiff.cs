namespace Student_Desktop_App.Core.Monitoring;

/// <summary>Apps that appeared and disappeared between two process snapshots (names, case-insensitive, sorted).</summary>
public sealed record ProcessChange(IReadOnlyList<string> Added, IReadOnlyList<string> Removed)
{
    public bool IsEmpty => Added.Count == 0 && Removed.Count == 0;
}

/// <summary>
/// Module 5 — compares two snapshots of the apps with a visible window, so the proctor's timeline shows
/// "started chrome" / "closed code" instead of the full list every 10 s (M5-U-04). Pure.
/// </summary>
public static class ProcessDiff
{
    public static ProcessChange Compute(IEnumerable<string> previous, IEnumerable<string> current)
    {
        var before = Normalise(previous);
        var after = Normalise(current);
        return new ProcessChange(
            after.Where(n => !before.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(),
            before.Where(n => !after.Contains(n)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Parses the comma-separated snapshot the heartbeat carries ("SmartExam,code,explorer").</summary>
    public static IReadOnlyList<string> Parse(string? snapshot) =>
        string.IsNullOrWhiteSpace(snapshot) || snapshot == "UNAVAILABLE"
            ? Array.Empty<string>()
            : snapshot.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static HashSet<string> Normalise(IEnumerable<string> names) =>
        names.Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
