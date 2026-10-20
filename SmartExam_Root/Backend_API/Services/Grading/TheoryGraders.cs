using System.Globalization;
using Backend_API.Models;

namespace Backend_API.Services.Grading;

/// <summary>A suggested mark for a theory answer.</summary>
public sealed record TheoryGrade(double SuggestedMarks, string Confidence, string Justification, string Engine);

/// <summary>
/// Module 11 — offline theory grading. Pure (M11-U-07, TheoryGraderTests).
/// <list type="bullet">
/// <item>With a model answer (<see cref="Question.ReferenceAnswer"/>): TF-IDF cosine similarity between the answer and
///       the model answer (corpus = just those two, so the score never depends on what other students wrote), combined
///       with key-term coverage: score = 0.7 × cosine + 0.3 × coverage. Key terms are the teacher's
///       <see cref="Question.RubricKeywords"/>, or else the model answer's five strongest terms.</item>
/// <item>With rubric keywords only: coverage alone.</item>
/// <item>With neither: no suggestion (0 marks, Low) — the teacher grades it.</item>
/// </list>
/// The method is lexical: a correct paraphrase in different words scores lower, so confidence is at most Medium unless
/// the score is extreme (≥ 0.9 or ≤ 0.1), and the justification always says how the number was reached.
/// </summary>
public static class TheoryGrader
{
    public const string EngineTfIdf = "TF-IDF (offline)";
    public const string EngineKeywords = "Rubric keywords (offline)";
    public const string EngineNone = "None (no model answer)";
    public const double CosineWeight = 0.7;
    public const double CoverageWeight = 0.3;
    public const int DefaultKeyTerms = 5;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being", "and", "or", "but", "if", "then", "so", "of",
        "to", "in", "on", "at", "by", "for", "with", "as", "from", "into", "it", "its", "this", "that", "these", "those",
        "which", "who", "whom", "what", "when", "where", "why", "how", "can", "could", "will", "would", "shall", "should",
        "may", "might", "must", "do", "does", "did", "has", "have", "had", "not", "no", "yes", "i", "we", "you", "they",
        "he", "she", "them", "their", "our", "your", "my", "me", "us", "also", "than", "very", "just", "there", "here",
        "both", "each", "while", "whereas", "such", "some", "any", "all", "more", "most", "other", "about", "over"
    };

    public static TheoryGrade Grade(Question question, string? answer)
    {
        var marks = question.Marks;
        if (string.IsNullOrWhiteSpace(answer))
            return new TheoryGrade(0, "High", "No answer was submitted.", EngineNone);

        var keywords = ParseKeywords(question.RubricKeywords);
        var reference = question.ReferenceAnswer;

        if (string.IsNullOrWhiteSpace(reference) && keywords.Count == 0)
            return new TheoryGrade(0, "Low",
                "No model answer or rubric keywords were set for this question, so it was not graded automatically — " +
                "teacher review required.", EngineNone);

        var answerTerms = Terms(answer);

        if (string.IsNullOrWhiteSpace(reference))
        {
            var (hit, missing) = Coverage(answer, answerTerms, keywords);
            var cov = keywords.Count == 0 ? 0 : (double)hit / keywords.Count;
            var suggested = TestCaseScorer.RoundToHalf(cov * marks);
            return new TheoryGrade(suggested, Confidence(cov),
                $"Key terms {hit}/{keywords.Count}{MissingText(missing)} (no model answer, so meaning was not compared). " +
                $"Suggested {Format(suggested)} / {marks} — teacher review required.", EngineKeywords);
        }

        var referenceTerms = Terms(reference);
        var cosine = Cosine(referenceTerms, answerTerms);
        if (keywords.Count == 0) keywords = TopTerms(referenceTerms, DefaultKeyTerms);
        var (hits, missed) = Coverage(answer, answerTerms, keywords);
        var coverage = keywords.Count == 0 ? cosine : (double)hits / keywords.Count;

        var score = Math.Clamp(CosineWeight * cosine + CoverageWeight * coverage, 0, 1);
        var mark = TestCaseScorer.RoundToHalf(score * marks);
        var text = $"Similarity to the model answer {cosine.ToString("0.00", CultureInfo.InvariantCulture)}; " +
                   $"key terms {hits}/{keywords.Count}{MissingText(missed)}. Score {score.ToString("0.00", CultureInfo.InvariantCulture)} " +
                   $"→ {Format(mark)} / {marks}. Lexical comparison only (a paraphrase scores lower) — " +
                   (Confidence(score) == "High" ? "teacher review recommended." : "teacher review required.");
        return new TheoryGrade(mark, Confidence(score), text, EngineTfIdf);
    }

    /// <summary>TF-IDF cosine between two texts, 0…1 (1 = same terms in the same proportions).</summary>
    public static double Similarity(string a, string b) => Cosine(Terms(a), Terms(b));

    /// <summary>Lower-case content terms (stop words removed, a light plural fold: "queues" → "queue").</summary>
    public static List<string> Terms(string? text) =>
        CodeTokenizer.TokenizeWords(text)
            .Select(t => Fold(t.Text))
            .Where(t => !StopWords.Contains(t))
            .ToList();

    private static string Fold(string word) =>
        word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) && !word.EndsWith("us", StringComparison.Ordinal)
            ? word[..^1]
            : word;

    private static double Cosine(List<string> a, List<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var tfA = a.GroupBy(t => t).ToDictionary(g => g.Key, g => (double)g.Count());
        var tfB = b.GroupBy(t => t).ToDictionary(g => g.Key, g => (double)g.Count());

        // Smooth IDF over the two-document corpus: idf = ln((1 + N) / (1 + df)) + 1 with N = 2.
        double Idf(string term) => Math.Log(3.0 / (1 + (tfA.ContainsKey(term) ? 1 : 0) + (tfB.ContainsKey(term) ? 1 : 0))) + 1;

        double dot = 0, normA = 0, normB = 0;
        foreach (var term in tfA.Keys.Union(tfB.Keys))
        {
            var idf = Idf(term);
            var wa = tfA.GetValueOrDefault(term) * idf;
            var wb = tfB.GetValueOrDefault(term) * idf;
            dot += wa * wb;
            normA += wa * wa;
            normB += wb * wb;
        }
        return normA == 0 || normB == 0 ? 0 : Math.Clamp(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)), 0, 1);
    }

    /// <summary>
    /// The model answer's strongest terms: most frequent first, ties by first appearance. Chosen from the model answer
    /// alone, so the key terms are the same for every student.
    /// </summary>
    private static List<string> TopTerms(List<string> reference, int count) =>
        reference
            .Select((term, index) => (term, index))
            .GroupBy(x => x.term)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Min(x => x.index))
            .Take(count)
            .Select(g => g.Key)
            .ToList();

    /// <summary>Keywords (single words or phrases) found in the answer, and the ones missing.</summary>
    private static (int Hits, List<string> Missing) Coverage(string answer, List<string> answerTerms, List<string> keywords)
    {
        var termSet = answerTerms.ToHashSet();
        var joined = " " + string.Join(' ', CodeTokenizer.TokenizeWords(answer).Select(t => Fold(t.Text))) + " ";
        var missing = new List<string>();
        var hits = 0;
        foreach (var keyword in keywords)
        {
            var words = CodeTokenizer.TokenizeWords(keyword).Select(t => Fold(t.Text)).ToList();
            var found = words.Count switch
            {
                0 => false,
                1 => termSet.Contains(words[0]) || joined.Contains(" " + words[0] + " ", StringComparison.Ordinal),
                _ => joined.Contains(" " + string.Join(' ', words) + " ", StringComparison.Ordinal)
            };
            if (found) hits++;
            else missing.Add(keyword.Trim());
        }
        return (hits, missing);
    }

    /// <summary>Comma / semicolon / newline separated rubric keywords, trimmed, lower-case, distinct.</summary>
    public static List<string> ParseKeywords(string? rubric) =>
        (rubric ?? string.Empty)
            .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(k => k.ToLowerInvariant())
            .Where(k => k.Length > 0)
            .Distinct()
            .ToList();

    private static string Confidence(double score) => score >= 0.9 || score <= 0.1 ? "High" : "Medium";

    private static string MissingText(List<string> missing) =>
        missing.Count == 0 ? string.Empty : $" (missing: {string.Join(", ", missing.Take(6))})";

    private static string Format(double marks) => marks.ToString("0.#", CultureInfo.InvariantCulture);
}
