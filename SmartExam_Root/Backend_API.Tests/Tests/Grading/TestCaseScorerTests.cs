using Backend_API.Models;
using Backend_API.Services.Grading;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Grading;

/// <summary>M11 — weighted test-case scoring (pure). M11-U-03…06 and the confidence / hidden-case rules.</summary>
public class TestCaseScorerTests
{
    private static ScoringCase Case(string expected, double weight = 1, bool hidden = false, bool edge = false, string input = "") =>
        new(Guid.NewGuid(), "case", input, expected, weight, hidden, edge);

    private static CaseRun Ok(int i, string stdout) => new(i, stdout, 0, false, false, 5);

    private static RunReport Compiled(params CaseRun[] runs) => new(true, null, runs, "test", 10);

    [Fact]
    public void M11_U_03_WeightedScore_TwoOfThreePassed_Weights112_IsFiftyPercent()
    {
        var cases = new[] { Case("1"), Case("2"), Case("3", weight: 2) };
        var score = TestCaseScorer.Score(20, cases, Compiled(Ok(0, "1"), Ok(1, "2"), Ok(2, "wrong")));

        score.PassRate.Should().BeApproximately(0.5, 1e-9);
        score.SuggestedMarks.Should().Be(10);
        score.Confidence.Should().Be("Medium", "a partial pass needs the teacher's eye");
        score.Justification.Should().Contain("2 passed").And.Contain("weighted 50");
    }

    [Fact]
    public void M11_U_04_CompileError_ZeroMarks_HighConfidence_CompilerOutputInJustification()
    {
        var report = new RunReport(false, "main.cpp:3:5: error: 'n' was not declared in this scope", [], "test", 10);
        var score = TestCaseScorer.Score(20, [Case("5")], report);

        score.SuggestedMarks.Should().Be(0);
        score.Confidence.Should().Be("High");
        score.Justification.Should().Contain("Compilation failed").And.Contain("'n' was not declared");
        score.Cases.Should().OnlyContain(c => c.Status == TestCaseResult.StatusNotRun);
    }

    [Fact]
    public void M11_U_05_Timeout_FailsOnlyThatCase_TheOthersStillCount()
    {
        var cases = new[] { Case("1"), Case("2") };
        var score = TestCaseScorer.Score(10, cases, Compiled(new CaseRun(0, "", null, true, false, 2000), Ok(1, "2")));

        score.Cases[0].Status.Should().Be(TestCaseResult.StatusTimedOut);
        score.Cases[1].Passed.Should().BeTrue();
        score.SuggestedMarks.Should().Be(5);
        score.Justification.Should().Contain("time limit exceeded");
    }

    [Fact]
    public void M11_U_06_TrailingNewline_Passes() =>
        TestCaseScorer.Score(10, [Case("5")], Compiled(Ok(0, "5\n"))).SuggestedMarks.Should().Be(10);

    [Fact]
    public void AllPassed_FullMarks_High_AndAllFailed_Zero_High()
    {
        var cases = new[] { Case("1"), Case("2") };
        var all = TestCaseScorer.Score(20, cases, Compiled(Ok(0, "1"), Ok(1, "2")));
        all.SuggestedMarks.Should().Be(20);
        all.Confidence.Should().Be("High");

        var none = TestCaseScorer.Score(20, cases, Compiled(Ok(0, "x"), Ok(1, "y")));
        none.SuggestedMarks.Should().Be(0);
        none.Confidence.Should().Be("High");
    }

    [Fact]
    public void HiddenCase_JustificationNeverQuotesItsInputOrOutput()
    {
        var cases = new[] { Case("3", input: "1 2"), Case("SECRET-EXPECTED", hidden: true, input: "SECRET-INPUT") };
        var score = TestCaseScorer.Score(10, cases, Compiled(Ok(0, "4"), Ok(1, "SECRET-ACTUAL")));

        score.Justification.Should().Contain("Hidden case #2: failed");
        score.Justification.Should().NotContain("SECRET-EXPECTED").And.NotContain("SECRET-INPUT").And.NotContain("SECRET-ACTUAL");
        score.Justification.Should().Contain("Case 1: failed — expected \"3\", got \"4\"", "visible cases explain the difference");
    }

    [Fact]
    public void RuntimeErrorAndMemory_AreFailures_WithTheirReason()
    {
        var cases = new[] { Case("1"), Case("2") };
        var score = TestCaseScorer.Score(10, cases, Compiled(new CaseRun(0, "1", 139, false, false, 3), new CaseRun(1, "", 137, false, true, 40)));

        score.Cases.Select(c => c.Status).Should().Equal(TestCaseResult.StatusRuntimeError, TestCaseResult.StatusMemoryExceeded);
        score.Justification.Should().Contain("exit code 139").And.Contain("memory limit exceeded");
        score.SuggestedMarks.Should().Be(0);
    }

    [Fact]
    public void NearCorrect_EightyPercentWithAtMostTwoFailures()
    {
        var cases = Enumerable.Range(0, 5).Select(i => Case(i.ToString())).ToArray();
        var score = TestCaseScorer.Score(10, cases, Compiled(Ok(0, "0"), Ok(1, "1"), Ok(2, "2"), Ok(3, "3"), Ok(4, "x")));

        score.NearCorrect.Should().BeTrue();
        score.SuggestedMarks.Should().Be(8);
        score.Justification.Should().Contain("Near-correct");
    }

    [Fact]
    public void Marks_RoundToTheNearestHalf()
    {
        // 1 of 3 equal cases on 10 marks = 3.33 → 3.5
        var cases = new[] { Case("1"), Case("2"), Case("3") };
        TestCaseScorer.Score(10, cases, Compiled(Ok(0, "1"), Ok(1, "x"), Ok(2, "x"))).SuggestedMarks.Should().Be(3.5);
        TestCaseScorer.RoundToHalf(2.25).Should().Be(2.5);
        TestCaseScorer.RoundToHalf(2.2).Should().Be(2);
    }

    [Fact]
    public void CompileOnly_NoTestCases_SuggestsNothing_LowConfidence()
    {
        var spec = new ScoringCase(null, TestCaseScorer.CompileOnlyCaseName, "", "", 1, false);
        var score = TestCaseScorer.Score(20, [spec], Compiled(Ok(0, "anything")), compileOnly: true);

        score.SuggestedMarks.Should().Be(0);
        score.Confidence.Should().Be("Low");
        score.Justification.Should().Contain("compile check only").And.Contain("teacher review required");
    }

    [Fact]
    public void MissingRun_CountsAsNotRun()
    {
        var score = TestCaseScorer.Score(10, [Case("1"), Case("2")], Compiled(Ok(0, "1")));
        score.Cases[1].Status.Should().Be(TestCaseResult.StatusNotRun);
        score.SuggestedMarks.Should().Be(5);
    }

    [Fact]
    public void Categories_GroupEdgeCasesSeparately()
    {
        var cases = new[] { Case("1"), Case("2", edge: true), Case("3", edge: true) };
        var score = TestCaseScorer.Score(9, cases, Compiled(Ok(0, "1"), Ok(1, "2"), Ok(2, "x")));
        score.Categories.Should().ContainEquivalentOf(new CategoryStat("edge", 1, 2));
        score.Categories.Should().ContainEquivalentOf(new CategoryStat("general", 1, 1));
    }
}
