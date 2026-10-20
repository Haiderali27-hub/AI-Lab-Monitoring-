using System.Text.RegularExpressions;

namespace Backend_API.Services.Grading;

/// <summary>Static facts about one coding answer, shown to the teacher next to the test results (never to the student).</summary>
public sealed record CodeAnalysis(
    int LoopCount,
    int MaxLoopNesting,
    string ComplexitySignal,
    bool UsesRecursion,
    IReadOnlyList<string> RecursiveFunctions,
    int IfCount,
    int NumericLiteralCount,
    IReadOnlyList<string> HardcodingSignals,
    string HardcodingSuspicion,
    double? PassRate,
    bool NearCorrect,
    IReadOnlyList<CategoryStat> Categories);

/// <summary>
/// Module 11 — lightweight static analysis of C / C++ / Python answers. Pure, regex- and token-based (no compiler), so
/// everything it reports is a <em>signal</em> for the teacher, not a verdict:
/// <list type="bullet">
/// <item>loops and their deepest nesting, mapped to a complexity signal (0 → O(1), 1 → O(n), 2 → O(n^2), more → O(n^3+));</item>
/// <item>recursion: a function whose own body calls it;</item>
/// <item>hard-coding suspicion, one point per signal — many <c>if</c>s, many numeric literals, literal lookup tables,
///       a test's expected output printed as a string literal, passing every visible case but failing hidden ones,
///       failing every edge case. Three or more signals = high, one or two = medium.</item>
/// </list>
/// </summary>
public static class CodeAnalyzer
{
    public const int ManyIfs = 10;
    public const int ManyNumericLiterals = 40;

    private static readonly Regex LiteralTable = new(@"\{\s*\{\s*-?\d+|\[\s*-?\d+\s*\]\s*=\s*-?\d+", RegexOptions.Compiled);
    private static readonly Regex PythonDef = new(@"^(?<indent>[ \t]*)def\s+(?<name>[A-Za-z_]\w*)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex StringLiteral = new("\"((?:[^\"\\\\\\n]|\\\\.)*)\"", RegexOptions.Compiled);

    private static readonly HashSet<string> NotFunctionNames = new(StringComparer.Ordinal)
    {
        "if", "for", "while", "switch", "catch", "return", "sizeof", "do", "else"
    };

    /// <param name="source">The student's code.</param>
    /// <param name="language">cpp / c / python (anything else is treated as C-like).</param>
    /// <param name="score">The test-case result, when the code ran — adds the result-based hard-coding signals.</param>
    public static CodeAnalysis Analyze(string? source, string language, CodeScore? score = null)
    {
        var code = source ?? string.Empty;
        var python = language == CodeLanguages.Python;
        List<CodeToken> tokens = python ? [] : CodeTokenizer.Tokenize(code);

        var (loops, nesting) = python ? PythonLoops(code) : CLoops(tokens);
        var recursive = python ? PythonRecursion(code) : CRecursion(tokens);
        var ifCount = python
            ? Regex.Matches(code, @"^\s*(if|elif)\b", RegexOptions.Multiline).Count
            : tokens.Count(t => t.Text == "if" && t.Kind == CodeTokenKind.Keyword);
        var numbers = python
            ? Regex.Matches(code, @"(?<![\w.])-?\d+(\.\d+)?").Count
            : tokens.Count(t => t.Kind == CodeTokenKind.Number);

        var signals = new List<string>();
        if (ifCount >= ManyIfs) signals.Add($"Many if statements ({ifCount}) — answers may be handled case by case.");
        if (numbers >= ManyNumericLiterals) signals.Add($"Many numeric literals ({numbers}).");
        if (LiteralTable.IsMatch(code)) signals.Add("Literal lookup table (values written into an array).");

        if (score is not null)
        {
            var printed = PrintedExpectedOutputs(code, score);
            if (printed) signals.Add("A test case's expected output appears as a string literal in the code.");

            var visible = score.Cases.Where(c => !c.Case.IsHidden).ToList();
            var hidden = score.Cases.Where(c => c.Case.IsHidden).ToList();
            if (visible.Count > 0 && visible.All(c => c.Passed) && hidden.Any(c => !c.Passed))
                signals.Add("Passes every visible test case but fails hidden ones.");

            var edge = score.Cases.Where(c => c.Case.IsEdgeCase).ToList();
            if (edge.Count > 0 && edge.All(c => !c.Passed) && score.Cases.Any(c => !c.Case.IsEdgeCase && c.Passed))
                signals.Add("Fails every edge case.");
        }

        var suspicion = signals.Count >= 3 ? "High" : signals.Count >= 1 ? "Medium" : "Low";
        var complexity = nesting switch
        {
            0 when recursive.Count > 0 => "Recursive (depends on the recurrence)",
            0 => "O(1)",
            1 => "O(n)",
            2 => "O(n^2)",
            _ => "O(n^3+)"
        };

        return new CodeAnalysis(loops, nesting, complexity, recursive.Count > 0, recursive, ifCount, numbers, signals, suspicion,
            score?.PassRate, score?.NearCorrect ?? false, score?.Categories ?? []);
    }

    // ── C-like loops ─────────────────────────────────────────────────────────

    private enum FrameKind { Block, LoopBlock, LoopStatement }
    private readonly record struct Frame(FrameKind Kind, bool IsDo);

    /// <summary>Counts for/while/do loops and the deepest nesting, following braces and single-statement bodies.</summary>
    private static (int Loops, int MaxNesting) CLoops(List<CodeToken> t)
    {
        var stack = new List<Frame>();
        var loops = 0;
        var max = 0;
        var expectDoWhile = false;

        int Depth() => stack.Count(f => f.Kind != FrameKind.Block);
        void PopStatements()
        {
            while (stack.Count > 0 && stack[^1].Kind == FrameKind.LoopStatement) stack.RemoveAt(stack.Count - 1);
        }

        for (var i = 0; i < t.Count; i++)
        {
            var text = t[i].Text;
            var keyword = t[i].Kind == CodeTokenKind.Keyword;

            if (keyword && text is "for" or "while" && i + 1 < t.Count && t[i + 1].Text == "(")
            {
                var close = MatchingParen(t, i + 1);
                var bodyStart = close + 1;
                if (text == "while" && bodyStart < t.Count && t[bodyStart].Text == ";" && expectDoWhile)
                {
                    // The `while (cond);` that ends a do-while: not a new loop.
                    expectDoWhile = false;
                    i = close;
                    continue;
                }
                expectDoWhile = false;
                loops++;
                if (bodyStart < t.Count && t[bodyStart].Text == "{")
                {
                    stack.Add(new Frame(FrameKind.LoopBlock, false));
                    i = bodyStart;
                }
                else
                {
                    stack.Add(new Frame(FrameKind.LoopStatement, false));
                    i = close;
                }
                max = Math.Max(max, Depth());
                continue;
            }

            if (keyword && text == "do")
            {
                loops++;
                if (i + 1 < t.Count && t[i + 1].Text == "{")
                {
                    stack.Add(new Frame(FrameKind.LoopBlock, true));
                    i++;
                }
                else
                {
                    stack.Add(new Frame(FrameKind.LoopStatement, true));
                }
                max = Math.Max(max, Depth());
                continue;
            }

            switch (text)
            {
                case "{":
                    stack.Add(new Frame(FrameKind.Block, false));
                    break;
                case "}":
                    PopStatements();
                    if (stack.Count > 0)
                    {
                        var closed = stack[^1];
                        stack.RemoveAt(stack.Count - 1);
                        expectDoWhile = closed.IsDo;
                    }
                    // A block that was the body of a single-statement loop (`for (...) if (x) { ... }`) ends that loop
                    // too, unless an else branch follows.
                    if (i + 1 >= t.Count || t[i + 1].Text != "else") PopStatements();
                    break;
                case ";":
                    PopStatements();
                    break;
            }
        }
        return (loops, max);
    }

    private static int MatchingParen(List<CodeToken> t, int open)
    {
        var depth = 0;
        for (var j = open; j < t.Count; j++)
        {
            if (t[j].Text == "(") depth++;
            else if (t[j].Text == ")" && --depth == 0) return j;
        }
        return t.Count - 1;
    }

    private static int MatchingBrace(List<CodeToken> t, int open)
    {
        var depth = 0;
        for (var j = open; j < t.Count; j++)
        {
            if (t[j].Text == "{") depth++;
            else if (t[j].Text == "}" && --depth == 0) return j;
        }
        return t.Count - 1;
    }

    /// <summary>Functions (name + parameter list + body) whose body calls the function itself.</summary>
    private static List<string> CRecursion(List<CodeToken> t)
    {
        var found = new List<string>();
        for (var i = 1; i + 1 < t.Count; i++)
        {
            if (t[i].Kind != CodeTokenKind.Identifier && !(t[i].Kind == CodeTokenKind.Keyword && t[i].Text is "main" or "max" or "min" or "sort" or "swap"))
                continue;
            if (NotFunctionNames.Contains(t[i].Text) || t[i + 1].Text != "(") continue;

            // A definition is preceded by a type-ish token (int, Node*, vector<int>&, ...).
            var prev = t[i - 1];
            if (!(prev.Kind is CodeTokenKind.Identifier or CodeTokenKind.Keyword || prev.Text is "*" or "&" or ">" or "::"))
                continue;
            if (prev.Kind == CodeTokenKind.Keyword && prev.Text is "return" or "else" or "new" or "delete") continue;

            var close = MatchingParen(t, i + 1);
            var bodyStart = close + 1;
            while (bodyStart < t.Count && t[bodyStart].Text is "const" or "noexcept" or "override") bodyStart++;
            if (bodyStart >= t.Count || t[bodyStart].Text != "{") continue;

            var bodyEnd = MatchingBrace(t, bodyStart);
            var name = t[i].Text;
            for (var j = bodyStart + 1; j + 1 < bodyEnd; j++)
            {
                if (t[j].Text == name && t[j + 1].Text == "(" && (j == 0 || t[j - 1].Text is not "." and not "->"))
                {
                    if (!found.Contains(name)) found.Add(name);
                    break;
                }
            }
            i = bodyEnd;
        }
        return found;
    }

    // ── Python ───────────────────────────────────────────────────────────────

    private static (int Loops, int MaxNesting) PythonLoops(string code)
    {
        var stack = new List<(int Indent, bool Loop)>();
        var loops = 0;
        var max = 0;
        foreach (var raw in code.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Split('#')[0].TrimEnd();
            if (line.Trim().Length == 0) continue;
            var indent = Indent(line);
            while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);

            var trimmed = line.Trim();
            if (!trimmed.EndsWith(':')) continue;
            var isLoop = trimmed.StartsWith("for ", StringComparison.Ordinal) || trimmed.StartsWith("while ", StringComparison.Ordinal);
            if (isLoop)
            {
                loops++;
                max = Math.Max(max, stack.Count(f => f.Loop) + 1);
            }
            stack.Add((indent, isLoop));
        }
        return (loops, max);
    }

    private static List<string> PythonRecursion(string code)
    {
        var found = new List<string>();
        var lines = code.Replace("\r\n", "\n").Split('\n');
        foreach (Match def in PythonDef.Matches(code))
        {
            var name = def.Groups["name"].Value;
            var indent = Indent(def.Groups["indent"].Value + "x");
            var startLine = code[..def.Index].Count(c => c == '\n') + 1;
            var call = new Regex($@"(?<![\w.]){Regex.Escape(name)}\s*\(");
            foreach (var line in lines.Skip(startLine))
            {
                if (line.Trim().Length == 0) continue;
                if (Indent(line) <= indent) break;
                if (call.IsMatch(line.Split('#')[0]))
                {
                    found.Add(name);
                    break;
                }
            }
        }
        return found;
    }

    private static int Indent(string line)
    {
        var n = 0;
        foreach (var c in line)
        {
            if (c == ' ') n++;
            else if (c == '\t') n += 4;
            else break;
        }
        return n;
    }

    private static bool PrintedExpectedOutputs(string code, CodeScore score)
    {
        var literals = StringLiteral.Matches(code).Select(m => m.Groups[1].Value.Replace("\\n", "\n").Trim()).ToHashSet();
        return score.Cases.Any(c =>
        {
            var expected = OutputComparer.Normalise(c.Case.ExpectedOutput).Trim();
            return expected.Length >= 2 && literals.Contains(expected);
        });
    }
}
