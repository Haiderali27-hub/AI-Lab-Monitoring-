using System.Text.Json.Serialization;

namespace Backend_API.Services.Grading;

/// <summary>A run of matching text: 1-based inclusive line ranges in each answer plus the excerpts.</summary>
public sealed record MatchedSegment(
    [property: JsonPropertyName("aStartLine")] int AStartLine,
    [property: JsonPropertyName("aEndLine")] int AEndLine,
    [property: JsonPropertyName("bStartLine")] int BStartLine,
    [property: JsonPropertyName("bEndLine")] int BEndLine,
    [property: JsonPropertyName("A")] string A,
    [property: JsonPropertyName("B")] string B);

public sealed record PlagiarismComparison(double Similarity, IReadOnlyList<MatchedSegment> Segments);

/// <summary>One winnowed fingerprint: the k-gram hash and the lines its tokens span.</summary>
public readonly record struct Fingerprint(ulong Hash, int StartLine, int EndLine);

/// <summary>An answer prepared for comparison (cache it: answers are frozen once a session is final).</summary>
public sealed class AnswerFingerprint
{
    public required string Text { get; init; }
    public required IReadOnlyList<string> Tokens { get; init; }
    public required IReadOnlyList<Fingerprint> Fingerprints { get; init; }
    public required HashSet<ulong> Hashes { get; init; }
}

/// <summary>
/// Module 11 — MOSS-style plagiarism detection (replaces the word-set Jaccard). Pure (M11-U-08…10, PlagiarismEngineTests).
/// <list type="number">
/// <item>Code is tokenised without comments and whitespace; identifiers become <c>ID</c>, numbers <c>NUM</c> and string
///       literals <c>STR</c> (keywords and common library names stay), so renaming variables or reformatting changes
///       nothing. Theory answers keep their lower-case words — normalising them too would make any two sentences look
///       alike.</item>
/// <item>Every k consecutive tokens (k = 5) are hashed; winnowing keeps the minimum hash of each window of w = 4
///       k-grams, which guarantees that any shared run of w + k − 1 = 8 tokens is detected.</item>
/// <item>Similarity = |shared fingerprints| / |all fingerprints| × 100 (Jaccard over the two fingerprint sets).
///       Answers shorter than k tokens fall back to Jaccard over their token sets.</item>
/// <item>Shared fingerprints are merged into line ranges for side-by-side highlighting.</item>
/// </list>
/// </summary>
public static class PlagiarismEngine
{
    public const int K = 5;
    public const int W = 4;
    public const int MaxSegments = 20;
    public const int MaxExcerptLength = 2000;

    public static AnswerFingerprint Fingerprint(string? text, bool isCode)
    {
        var source = text ?? string.Empty;
        var raw = isCode ? CodeTokenizer.Tokenize(source) : CodeTokenizer.TokenizeWords(source);
        var tokens = raw.Select(t => isCode ? Normalise(t) : t.Text).ToList();
        var lines = raw.Select(t => t.Line).ToList();

        var fingerprints = Winnow(tokens, lines);
        return new AnswerFingerprint
        {
            Text = source,
            Tokens = tokens,
            Fingerprints = fingerprints,
            Hashes = fingerprints.Select(f => f.Hash).ToHashSet()
        };
    }

    public static PlagiarismComparison Compare(string? a, string? b, bool isCode) =>
        Compare(Fingerprint(a, isCode), Fingerprint(b, isCode));

    public static PlagiarismComparison Compare(AnswerFingerprint a, AnswerFingerprint b)
    {
        if (a.Tokens.Count == 0 || b.Tokens.Count == 0) return new PlagiarismComparison(0, []);

        if (a.Tokens.Count < K || b.Tokens.Count < K)
        {
            // Too short to fingerprint: plain token-set Jaccard.
            var sa = a.Tokens.ToHashSet(StringComparer.Ordinal);
            var sb = b.Tokens.ToHashSet(StringComparer.Ordinal);
            var union = sa.Union(sb).Count();
            var score = union == 0 ? 0 : 100.0 * sa.Intersect(sb).Count() / union;
            MatchedSegment[] whole = score > 0 ? [Whole(a, b)] : [];
            return new PlagiarismComparison(score, whole);
        }

        var shared = a.Hashes.Intersect(b.Hashes).ToHashSet();
        var all = a.Hashes.Union(b.Hashes).Count();
        var similarity = all == 0 ? 0 : 100.0 * shared.Count / all;
        return new PlagiarismComparison(similarity, Segments(a, b, shared));
    }

    /// <summary>Code token → comparison token: identifiers ID, numbers NUM, strings STR, the rest as written.</summary>
    private static string Normalise(CodeToken t) => t.Kind switch
    {
        CodeTokenKind.Identifier => "ID",
        CodeTokenKind.Number => "NUM",
        CodeTokenKind.String => "STR",
        _ => t.Text
    };

    private static List<Fingerprint> Winnow(IReadOnlyList<string> tokens, IReadOnlyList<int> lines)
    {
        var result = new List<Fingerprint>();
        if (tokens.Count < K) return result;

        var grams = new List<Fingerprint>(tokens.Count - K + 1);
        for (var i = 0; i + K <= tokens.Count; i++)
            grams.Add(new Fingerprint(Hash(tokens, i, K), lines[i], lines[i + K - 1]));

        if (grams.Count <= W)
        {
            // A single window: keep its minimum.
            result.Add(grams.MinBy(g => g.Hash));
            return result;
        }

        var lastPicked = -1;
        for (var start = 0; start + W <= grams.Count; start++)
        {
            // Rightmost minimum of the window (the standard tie-break), recorded once per position.
            var min = start;
            for (var j = start + 1; j < start + W; j++)
                if (grams[j].Hash <= grams[min].Hash) min = j;
            if (min != lastPicked)
            {
                result.Add(grams[min]);
                lastPicked = min;
            }
        }
        return result;
    }

    // FNV-1a 64 over the k tokens: deterministic across processes (string.GetHashCode is randomised per run).
    private static ulong Hash(IReadOnlyList<string> tokens, int start, int count)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        for (var i = start; i < start + count; i++)
        {
            foreach (var ch in tokens[i])
            {
                hash ^= ch;
                hash *= prime;
            }
            hash ^= 0x1F;   // token separator: "ab"+"c" must differ from "a"+"bc"
            hash *= prime;
        }
        return hash;
    }

    private static List<MatchedSegment> Segments(AnswerFingerprint a, AnswerFingerprint b, HashSet<ulong> shared)
    {
        if (shared.Count == 0) return [];

        // First occurrence of each shared fingerprint in B, then walk A in order and merge neighbouring matches.
        var inB = new Dictionary<ulong, Fingerprint>();
        foreach (var f in b.Fingerprints)
            if (shared.Contains(f.Hash)) inB.TryAdd(f.Hash, f);

        var matches = a.Fingerprints
            .Where(f => shared.Contains(f.Hash))
            .Select(f => (A: f, B: inB[f.Hash]))
            .OrderBy(m => m.A.StartLine).ThenBy(m => m.B.StartLine)
            .ToList();

        var ranges = new List<(int AS, int AE, int BS, int BE)>();
        foreach (var (fa, fb) in matches)
        {
            if (ranges.Count > 0)
            {
                var last = ranges[^1];
                var continuesA = fa.StartLine <= last.AE + 1;
                var continuesB = fb.StartLine <= last.BE + 1 && fb.EndLine >= last.BS - 1;
                if (continuesA && continuesB)
                {
                    ranges[^1] = (last.AS, Math.Max(last.AE, fa.EndLine), Math.Min(last.BS, fb.StartLine), Math.Max(last.BE, fb.EndLine));
                    continue;
                }
            }
            ranges.Add((fa.StartLine, fa.EndLine, fb.StartLine, fb.EndLine));
        }

        var linesA = SplitLines(a.Text);
        var linesB = SplitLines(b.Text);
        return ranges.Take(MaxSegments)
            .Select(r => new MatchedSegment(r.AS, r.AE, r.BS, r.BE, Excerpt(linesA, r.AS, r.AE), Excerpt(linesB, r.BS, r.BE)))
            .ToList();
    }

    private static MatchedSegment Whole(AnswerFingerprint a, AnswerFingerprint b)
    {
        var la = SplitLines(a.Text);
        var lb = SplitLines(b.Text);
        return new MatchedSegment(1, Math.Max(1, la.Length), 1, Math.Max(1, lb.Length),
            Excerpt(la, 1, la.Length), Excerpt(lb, 1, lb.Length));
    }

    private static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Split('\n');

    private static string Excerpt(string[] lines, int from, int to)
    {
        var start = Math.Clamp(from, 1, Math.Max(1, lines.Length));
        var end = Math.Clamp(to, start, Math.Max(1, lines.Length));
        var text = string.Join("\n", lines.Skip(start - 1).Take(end - start + 1));
        return text.Length > MaxExcerptLength ? text[..MaxExcerptLength] + "…" : text;
    }
}
