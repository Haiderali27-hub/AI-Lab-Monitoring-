using System.Globalization;

namespace Backend_API.Services.Grading;

/// <summary>How a test case's output is compared (per test case, set by the teacher).</summary>
public sealed record CompareOptions(bool IgnoreCase = false, double? FloatTolerance = null)
{
    public static readonly CompareOptions Default = new();
}

/// <summary>
/// Module 11 — decides whether a program's output matches the expected output. Lenient about whitespace only,
/// in this order: (1) exact after CRLF→LF and trailing whitespace; (2) line by line with each line trimmed and
/// trailing blank lines ignored; (3) when the case allows it, ignoring letter case; (4) when the case sets a
/// tolerance, token by token where numbers may differ by at most the tolerance (other tokens must match).
/// Pure and deterministic: unit tests M11-U-06 and OutputComparerTests.
/// </summary>
public static class OutputComparer
{
    public const string ModeExact = "exact";
    public const string ModeTrimmedLines = "trimmed lines";
    public const string ModeIgnoreCase = "ignore case";
    public const string ModeNumeric = "numeric tolerance";

    public static (bool Passed, string? Mode) Compare(string? expected, string? actual, CompareOptions? options = null)
    {
        options ??= CompareOptions.Default;
        var e = Normalise(expected);
        var a = Normalise(actual);

        if (string.Equals(e, a, StringComparison.Ordinal)) return (true, ModeExact);

        var expectedLines = TrimmedLines(e);
        var actualLines = TrimmedLines(a);
        if (expectedLines.SequenceEqual(actualLines, StringComparer.Ordinal)) return (true, ModeTrimmedLines);

        if (options.IgnoreCase && expectedLines.SequenceEqual(actualLines, StringComparer.OrdinalIgnoreCase))
            return (true, ModeIgnoreCase);

        if (options.FloatTolerance is { } tolerance && tolerance >= 0 && TokensMatch(e, a, tolerance, options.IgnoreCase))
            return (true, ModeNumeric);

        return (false, null);
    }

    /// <summary>CRLF / CR → LF, then trailing whitespace (spaces, tabs, newlines) removed.</summary>
    public static string Normalise(string? text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd();

    private static List<string> TrimmedLines(string normalised)
    {
        var lines = normalised.Split('\n').Select(l => l.Trim()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static bool TokensMatch(string expected, string actual, double tolerance, bool ignoreCase)
    {
        var e = expected.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var a = actual.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (e.Length != a.Length) return false;

        for (var i = 0; i < e.Length; i++)
        {
            if (TryNumber(e[i], out var x) && TryNumber(a[i], out var y))
            {
                if (double.IsNaN(x) || double.IsNaN(y))
                {
                    if (!(double.IsNaN(x) && double.IsNaN(y))) return false;
                    continue;
                }
                if (double.IsInfinity(x) || double.IsInfinity(y))
                {
                    if (x != y) return false;
                    continue;
                }
                // A tiny relative slack absorbs binary rounding of the tolerance itself (0.1 vs 0.30000000000000004).
                if (Math.Abs(x - y) > tolerance + 1e-12 * Math.Max(1, Math.Abs(x))) return false;
                continue;
            }

            if (!string.Equals(e[i], a[i], ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    private static bool TryNumber(string token, out double value) =>
        double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
