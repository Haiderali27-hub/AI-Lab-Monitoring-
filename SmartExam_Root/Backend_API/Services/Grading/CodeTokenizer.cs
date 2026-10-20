using System.Text;

namespace Backend_API.Services.Grading;

public enum CodeTokenKind { Keyword, Identifier, Number, String, Operator }

/// <summary>One lexical token with the 1-based source line it starts on.</summary>
public readonly record struct CodeToken(CodeTokenKind Kind, string Text, int Line);

/// <summary>
/// Module 11 — a small, language-tolerant lexer for C, C++, Java and Python answers, shared by the plagiarism
/// engine and the static analyser. It drops comments (<c>//</c>, <c>/* */</c>, and <c>#</c> lines — preprocessor
/// directives are boilerplate every student shares — or Python <c>#</c> comments), keeps line numbers, and folds
/// string/char literals into one token. It never throws on malformed input (an unterminated string or comment just
/// ends at the end of the text).
/// </summary>
public static class CodeTokenizer
{
    /// <summary>Keywords and common library names kept as themselves when identifiers are normalised to ID.</summary>
    public static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        // C / C++
        "auto", "bool", "break", "case", "catch", "char", "class", "const", "constexpr", "continue", "default", "delete",
        "do", "double", "else", "enum", "explicit", "extern", "false", "float", "for", "friend", "goto", "if", "inline",
        "int", "long", "mutable", "namespace", "new", "nullptr", "operator", "private", "protected", "public", "register",
        "return", "short", "signed", "sizeof", "static", "struct", "switch", "template", "this", "throw", "true", "try",
        "typedef", "typename", "union", "unsigned", "using", "virtual", "void", "volatile", "while", "NULL",
        // Java
        "boolean", "byte", "extends", "final", "finally", "implements", "import", "instanceof", "interface", "package",
        "super", "synchronized", "throws", "String", "System",
        // Python
        "and", "as", "assert", "def", "del", "elif", "except", "from", "global", "in", "is", "lambda", "None", "nonlocal",
        "not", "or", "pass", "raise", "with", "yield", "True", "False", "print", "range", "len", "input",
        // Library names whose identity carries meaning (renaming them is not a disguise)
        "std", "cout", "cin", "endl", "printf", "scanf", "puts", "getline", "vector", "string", "map", "set", "pair",
        "malloc", "free", "main", "max", "min", "swap", "sort", "push_back", "size"
    };

    private static readonly string[] MultiCharOperators =
    [
        "<<=", ">>=", "...", "->*", "<<", ">>", "<=", ">=", "==", "!=", "&&", "||", "++", "--", "+=", "-=", "*=", "/=",
        "%=", "&=", "|=", "^=", "->", "::", "**", "//"
    ];

    public static List<CodeToken> Tokenize(string? source)
    {
        var tokens = new List<CodeToken>();
        if (string.IsNullOrEmpty(source)) return tokens;

        var s = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var line = 1;
        var i = 0;

        while (i < s.Length)
        {
            var c = s[i];

            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }

            // Comments: //…, /*…*/, and # to end of line (preprocessor directive or Python comment).
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/' || c == '#')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                i += 2;
                while (i < s.Length && !(s[i] == '*' && i + 1 < s.Length && s[i + 1] == '/'))
                {
                    if (s[i] == '\n') line++;
                    i++;
                }
                i = Math.Min(s.Length, i + 2);
                continue;
            }

            var startLine = line;

            if (c is '"' or '\'')
            {
                // Python triple-quoted strings span lines.
                var triple = i + 2 < s.Length && s[i + 1] == c && s[i + 2] == c;
                i += triple ? 3 : 1;
                while (i < s.Length)
                {
                    if (s[i] == '\\')
                    {
                        if (i + 1 < s.Length && s[i + 1] == '\n') line++;
                        i += 2;
                        continue;
                    }
                    if (s[i] == '\n')
                    {
                        if (!triple) break;     // unterminated single-line literal: stop at the line end
                        line++;
                    }
                    if (triple ? i + 2 < s.Length && s[i] == c && s[i + 1] == c && s[i + 2] == c : s[i] == c)
                    {
                        i += triple ? 3 : 1;
                        break;
                    }
                    i++;
                }
                tokens.Add(new CodeToken(CodeTokenKind.String, "STR", startLine));
                continue;
            }

            if (char.IsDigit(c) || c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1]))
            {
                var start = i;
                var hex = c == '0' && i + 1 < s.Length && s[i + 1] is 'x' or 'X';
                while (i < s.Length)
                {
                    var d = s[i];
                    // Digits and suffixes (1e9, 10LL, 0x1F, 1'000), and a sign right after a decimal exponent (1e-9).
                    var exponentSign = d is '+' or '-' && !hex && s[i - 1] is 'e' or 'E';
                    if (!(char.IsLetterOrDigit(d) || d is '.' or '_' or '\'' || exponentSign)) break;
                    i++;
                }
                tokens.Add(new CodeToken(CodeTokenKind.Number, s[start..i], startLine));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                var word = s[start..i];
                tokens.Add(new CodeToken(Keywords.Contains(word) ? CodeTokenKind.Keyword : CodeTokenKind.Identifier, word, startLine));
                continue;
            }

            var op = MultiCharOperators.FirstOrDefault(o => string.CompareOrdinal(s, i, o, 0, o.Length) == 0);
            if (op is not null && op != "//")
            {
                tokens.Add(new CodeToken(CodeTokenKind.Operator, op, startLine));
                i += op.Length;
                continue;
            }
            tokens.Add(new CodeToken(CodeTokenKind.Operator, c.ToString(), startLine));
            i++;
        }

        return tokens;
    }

    /// <summary>Lower-case word tokens of prose (theory answers), with line numbers.</summary>
    public static List<CodeToken> TokenizeWords(string? text)
    {
        var tokens = new List<CodeToken>();
        if (string.IsNullOrEmpty(text)) return tokens;
        var line = 1;
        var sb = new StringBuilder();
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (char.IsLetterOrDigit(ch)) { sb.Append(char.ToLowerInvariant(ch)); continue; }
            if (sb.Length > 0) { tokens.Add(new CodeToken(CodeTokenKind.Identifier, sb.ToString(), line)); sb.Clear(); }
            if (ch == '\n') line++;
        }
        if (sb.Length > 0) tokens.Add(new CodeToken(CodeTokenKind.Identifier, sb.ToString(), line));
        return tokens;
    }
}
