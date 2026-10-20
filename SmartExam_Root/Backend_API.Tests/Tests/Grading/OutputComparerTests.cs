using Backend_API.Services.Grading;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Grading;

/// <summary>M11 — output comparison (pure). M11-U-06 plus the tolerance rules.</summary>
public class OutputComparerTests
{
    [Fact]
    public void M11_U_06_TrailingNewline_IsIgnored()
    {
        var (passed, mode) = OutputComparer.Compare("5", "5\n");
        passed.Should().BeTrue();
        mode.Should().Be(OutputComparer.ModeExact);
    }

    [Theory]
    [InlineData("1 2\n3", "1 2\r\n3\r\n")]          // Windows line endings
    [InlineData("1 2\n3", "1 2   \n3\t\n\n\n")]     // trailing spaces and blank lines
    public void LineEndingsAndTrailingWhitespace_Pass(string expected, string actual) =>
        OutputComparer.Compare(expected, actual).Passed.Should().BeTrue();

    [Fact]
    public void LeadingSpacesPerLine_PassAsTrimmedLines()
    {
        var (passed, mode) = OutputComparer.Compare("a\nb", "  a\n  b");
        passed.Should().BeTrue();
        mode.Should().Be(OutputComparer.ModeTrimmedLines);
    }

    [Fact]
    public void DifferentValue_Fails() => OutputComparer.Compare("5", "6").Passed.Should().BeFalse();

    [Fact]
    public void InnerSpacing_StillMatters() => OutputComparer.Compare("1 2", "1  2").Passed.Should().BeFalse();

    [Fact]
    public void LetterCase_OnlyIgnoredWhenTheCaseAllowsIt()
    {
        OutputComparer.Compare("YES", "yes").Passed.Should().BeFalse();
        var (passed, mode) = OutputComparer.Compare("YES", "yes", new CompareOptions(IgnoreCase: true));
        passed.Should().BeTrue();
        mode.Should().Be(OutputComparer.ModeIgnoreCase);
    }

    [Fact]
    public void Numbers_WithinTolerance_Pass_TokenByToken()
    {
        var options = new CompareOptions(FloatTolerance: 0.001);
        var (passed, mode) = OutputComparer.Compare("area 3.14159\n2.5", "area 3.1416 2.5000", options);
        passed.Should().BeTrue();
        mode.Should().Be(OutputComparer.ModeNumeric);

        OutputComparer.Compare("3.14159", "3.1416").Passed.Should().BeFalse("no tolerance means exact text");
        OutputComparer.Compare("3.14159", "3.15", options).Passed.Should().BeFalse("outside the tolerance");
        OutputComparer.Compare("area 3.14", "volume 3.14", options).Passed.Should().BeFalse("words must still match");
        OutputComparer.Compare("1 2", "1 2 3", options).Passed.Should().BeFalse("token counts differ");
    }

    [Fact]
    public void ToleranceItself_IsInclusive_DespiteBinaryRounding() =>
        OutputComparer.Compare("0.3", "0.4", new CompareOptions(FloatTolerance: 0.1)).Passed.Should().BeTrue();

    [Fact]
    public void NullOutput_IsEmpty()
    {
        OutputComparer.Compare("", null).Passed.Should().BeTrue();
        OutputComparer.Compare("x", null).Passed.Should().BeFalse();
    }
}
