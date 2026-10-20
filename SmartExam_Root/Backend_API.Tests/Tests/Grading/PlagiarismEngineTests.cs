using Backend_API.Services.Grading;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Grading;

/// <summary>M11 — token-normalised k-gram winnowing (pure). The API-level M11-U-08…10 live in the acceptance tests.</summary>
public class PlagiarismEngineTests
{
    private const string SumLoop = """
        int sum(int* arr, int n) {
            int total = 0;
            for (int i = 0; i < n; i++) {
                total += arr[i];
            }
            return total;
        }
        """;

    [Fact]
    public void RenamedIdentifiers_AreTheSameProgram()
    {
        const string renamed = """
            int addAll(int* values, int count) {
                int acc = 0;
                for (int k = 0; k < count; k++) {
                    acc += values[k];
                }
                return acc;
            }
            """;
        PlagiarismEngine.Compare(SumLoop, renamed, isCode: true).Similarity.Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact]
    public void CommentsAndLayout_AreIgnored()
    {
        const string squashed = "// header\n#include <stdio.h>\nint sum(int*arr,int n){/*x*/int total=0;for(int i=0;i<n;i++){total+=arr[i];}return total;}";
        PlagiarismEngine.Compare(SumLoop, squashed, isCode: true).Similarity.Should().Be(100);
    }

    [Fact]
    public void DifferentAlgorithm_IsFarApart()
    {
        const string recursive = """
            int sum(int* arr, int n) {
                if (n == 0) return 0;
                return arr[n - 1] + sum(arr, n - 1);
            }
            """;
        PlagiarismEngine.Compare(SumLoop, recursive, isCode: true).Similarity.Should().BeLessThan(30);
    }

    [Fact]
    public void Identical_Is100_WithLineRangedSegments()
    {
        var result = PlagiarismEngine.Compare(SumLoop, SumLoop, isCode: true);
        result.Similarity.Should().Be(100);
        result.Segments.Should().NotBeEmpty();
        foreach (var s in result.Segments)
        {
            s.AStartLine.Should().BeGreaterThan(0);
            s.AEndLine.Should().BeGreaterThanOrEqualTo(s.AStartLine);
            s.BEndLine.Should().BeGreaterThanOrEqualTo(s.BStartLine);
            s.A.Should().NotBeEmpty();
        }
        result.Segments.First().AStartLine.Should().Be(1);
        result.Segments.Max(s => s.AEndLine).Should().BeGreaterThanOrEqualTo(6, "winnowing samples the k-grams, the last lines are covered");
    }

    [Fact]
    public void CopiedBlock_InsideLargerProgram_IsLocated()
    {
        var a = "int x;\nint y;\n" + SumLoop;           // the copied function starts on line 3
        var b = SumLoop + "\nint main() { return 0; }";   // and on line 1 here
        var result = PlagiarismEngine.Compare(a, b, isCode: true);
        result.Similarity.Should().BeGreaterThan(50);
        var seg = result.Segments.OrderByDescending(s => s.AEndLine - s.AStartLine).First();
        seg.AStartLine.Should().BeInRange(3, 4);
        seg.BStartLine.Should().BeInRange(1, 2);
    }

    [Fact]
    public void Theory_KeepsWords_SoDifferentSentencesStayApart()
    {
        PlagiarismEngine.Compare("A stack follows LIFO order so the last pushed element is popped first",
                "Photosynthesis in green plants converts light energy into chemical energy", isCode: false)
            .Similarity.Should().Be(0);
        PlagiarismEngine.Compare("identical answer text for the heat map", "Identical answer text, for the heat map!", isCode: false)
            .Similarity.Should().Be(100);
    }

    [Fact]
    public void ShortAnswers_FallBackToTokenJaccard()
    {
        PlagiarismEngine.Compare("LIFO", "LIFO", isCode: false).Similarity.Should().Be(100);
        PlagiarismEngine.Compare("LIFO FIFO", "LIFO", isCode: false).Similarity.Should().Be(50);
    }

    [Fact]
    public void Empty_IsZero()
    {
        PlagiarismEngine.Compare("", SumLoop, isCode: true).Similarity.Should().Be(0);
        PlagiarismEngine.Compare(null, null, isCode: false).Similarity.Should().Be(0);
    }

    [Fact]
    public void Fingerprints_AreDeterministic()
    {
        var a = PlagiarismEngine.Fingerprint(SumLoop, true);
        var b = PlagiarismEngine.Fingerprint(SumLoop, true);
        a.Hashes.Should().BeEquivalentTo(b.Hashes);
        a.Fingerprints.Should().NotBeEmpty();
    }

    [Fact]
    public void Tokenizer_DropsCommentsAndDirectives_KeepsLines()
    {
        var tokens = CodeTokenizer.Tokenize("#include <x>\n// c\nint a = 5; /* multi\nline */ char* s = \"hi // not a comment\";");
        tokens.Select(t => t.Text).Should().Equal("int", "a", "=", "5", ";", "char", "*", "s", "=", "STR", ";");
        tokens.First().Line.Should().Be(3);
        tokens.Last().Line.Should().Be(4);
        tokens.Single(t => t.Text == "5").Kind.Should().Be(CodeTokenKind.Number);
        CodeTokenizer.Tokenize("x += 1e-9;").Select(t => t.Text).Should().Equal("x", "+=", "1e-9", ";");
    }
}
