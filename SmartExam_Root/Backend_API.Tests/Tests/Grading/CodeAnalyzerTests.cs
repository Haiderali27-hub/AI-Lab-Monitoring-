using Backend_API.Services.Grading;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Grading;

/// <summary>M11 — static analysis signals: loops / complexity, recursion, hard-coding (pure).</summary>
public class CodeAnalyzerTests
{
    [Theory]
    [InlineData("int main(){ int x = 1; return x; }", 0, 0, "O(1)")]
    [InlineData("int main(){ for(int i=0;i<n;i++){ s+=i; } }", 1, 1, "O(n)")]
    [InlineData("int main(){ for(int i=0;i<n;i++){ for(int j=0;j<n;j++){ s++; } } }", 2, 2, "O(n^2)")]
    [InlineData("int main(){ for(int i=0;i<n;i++) for(int j=0;j<n;j++) for(int k=0;k<n;k++) s++; }", 3, 3, "O(n^3+)")]
    [InlineData("int main(){ for(;;){} while(x){} }", 2, 1, "O(n)")]                       // sequential loops
    [InlineData("int main(){ do { x--; } while (x > 0); }", 1, 1, "O(n)")]                  // do-while is one loop
    [InlineData("int main(){ for(i=0;i<n;i++) if (a[i]) { b++; } for(j=0;j<n;j++){} }", 2, 1, "O(n)")]
    [InlineData("int main(){ while (n--) { for (int i=0;i<3;i++) {} } }", 2, 2, "O(n^2)")]
    public void Loops_AndNesting(string code, int loops, int nesting, string complexity)
    {
        var a = CodeAnalyzer.Analyze(code, CodeLanguages.Cpp);
        a.LoopCount.Should().Be(loops);
        a.MaxLoopNesting.Should().Be(nesting);
        a.ComplexitySignal.Should().Be(complexity);
    }

    [Fact]
    public void Recursion_IsDetected_OnlyWhenTheFunctionCallsItself()
    {
        const string recursive = "int sum(int* arr, int n) {\n if (n == 0) return 0;\n return arr[n-1] + sum(arr, n-1);\n}\nint main(){ return sum(0,0); }";
        var a = CodeAnalyzer.Analyze(recursive, CodeLanguages.Cpp);
        a.UsesRecursion.Should().BeTrue();
        a.RecursiveFunctions.Should().Equal("sum");
        a.ComplexitySignal.Should().StartWith("Recursive");

        CodeAnalyzer.Analyze("int f(int x){ return g(x); }\nint g(int y){ return y; }", CodeLanguages.Cpp).UsesRecursion.Should().BeFalse();
    }

    [Fact]
    public void Python_LoopsAndRecursion()
    {
        const string code = "def fact(n):\n    if n == 0:\n        return 1\n    return n * fact(n - 1)\n\nfor i in range(3):\n    for j in range(3):\n        print(i, j)\n";
        var a = CodeAnalyzer.Analyze(code, CodeLanguages.Python);
        a.LoopCount.Should().Be(2);
        a.MaxLoopNesting.Should().Be(2);
        a.RecursiveFunctions.Should().Equal("fact");
    }

    [Fact]
    public void Hardcoding_ManySignals_IsHighSuspicion()
    {
        var ifs = string.Concat(Enumerable.Range(0, 12).Select(i => $"if (n == {i}) cout << \"{i * i}\";\n"));
        var code = "int table[] = {0};\nint main(){ int n; cin >> n; table[3] = 9;\n" + ifs + "}";
        var cases = new[] { new ScoringCase(null, "c", "4", "16", 1, false), new ScoringCase(null, "h", "40", "1600", 1, true) };
        var score = TestCaseScorer.Score(10, cases, new RunReport(true, null, [new CaseRun(0, "16", 0, false, false, 1), new CaseRun(1, "", 0, false, false, 1)], "t", 1));

        var a = CodeAnalyzer.Analyze(code, CodeLanguages.Cpp, score);
        a.IfCount.Should().Be(12);
        a.HardcodingSignals.Should().Contain(s => s.StartsWith("Many if statements"));
        a.HardcodingSignals.Should().Contain(s => s.StartsWith("Literal lookup table"));
        a.HardcodingSignals.Should().Contain(s => s.Contains("expected output appears as a string literal"));
        a.HardcodingSignals.Should().Contain(s => s.StartsWith("Passes every visible test case but fails hidden"));
        a.HardcodingSuspicion.Should().Be("High");
    }

    [Fact]
    public void CleanSolution_LowSuspicion()
    {
        var a = CodeAnalyzer.Analyze("int main(){ int n,x,m=-1e9; std::cin>>n; while(n--){ std::cin>>x; if(x>m) m=x; } std::cout<<m; }", CodeLanguages.Cpp);
        a.HardcodingSignals.Should().BeEmpty();
        a.HardcodingSuspicion.Should().Be("Low");
        a.LoopCount.Should().Be(1);
    }

    [Fact]
    public void FailsEveryEdgeCase_IsASignal()
    {
        var cases = new[]
        {
            new ScoringCase(null, "a", "", "1", 1, false),
            new ScoringCase(null, "b", "", "2", 1, false, IsEdgeCase: true)
        };
        var score = TestCaseScorer.Score(10, cases, new RunReport(true, null, [new CaseRun(0, "1", 0, false, false, 1), new CaseRun(1, "x", 0, false, false, 1)], "t", 1));
        CodeAnalyzer.Analyze("int main(){}", CodeLanguages.Cpp, score).HardcodingSignals.Should().Contain("Fails every edge case.");
    }

    [Theory]
    [InlineData(null, "#include <iostream>\nint main(){std::cout<<1;}", "cpp")]
    [InlineData(null, "#include <stdlib.h>\nint main(){ char* p = malloc(4); }", "c")]
    [InlineData(null, "print(sum(map(int, input().split())))", "python")]
    [InlineData(null, "int main(){ return 0; }", "cpp")]
    [InlineData("C", "#include <iostream>", "c")]
    [InlineData("c++", "anything", "cpp")]
    public void Language_DetectionAndNormalisation(string? configured, string source, string expected) =>
        CodeLanguages.Resolve(configured, source).Should().Be(expected);
}
