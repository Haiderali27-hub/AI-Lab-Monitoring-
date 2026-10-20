using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services.Grading;
using FluentAssertions;

namespace Backend_API.Tests.Tests.Grading;

/// <summary>M11 — offline theory grading: TF-IDF cosine + key-term coverage (pure). M11-U-07.</summary>
public class TheoryGraderTests
{
    private static Question Q(string? reference, string? rubric = null, int marks = 10) => new()
    {
        Type = QuestionType.Theory,
        BodyText = "Explain stack vs queue.",
        Marks = marks,
        ReferenceAnswer = reference,
        RubricKeywords = rubric
    };

    [Fact]
    public void M11_U_07_IdenticalText_CosineIsOne_FullMarks()
    {
        const string text = "A stack is LIFO and a queue is FIFO.";
        TheoryGrader.Similarity(text, text).Should().BeApproximately(1.0, 1e-9);

        var grade = TheoryGrader.Grade(Q(text), text);
        grade.SuggestedMarks.Should().Be(10);
        grade.Confidence.Should().Be("High");
        grade.Engine.Should().Be(TheoryGrader.EngineTfIdf);
    }

    [Fact]
    public void Paraphrase_WithTheSameKeyTerms_StillScoresFull()
    {
        // Stop words and punctuation do not count; "Stack is LIFO, queue is FIFO." has the same content terms.
        var grade = TheoryGrader.Grade(Q("Stack is LIFO, queue is FIFO."), "A stack is LIFO and a queue is FIFO.");
        grade.SuggestedMarks.Should().Be(10);
        grade.Justification.Should().Contain("Similarity to the model answer 1.00").And.Contain("key terms 4/4");
    }

    [Fact]
    public void PartialAnswer_PartialMarks_MediumConfidence_NamesTheMissingTerms()
    {
        var grade = TheoryGrader.Grade(Q("Stack is LIFO, queue is FIFO."), "A stack is LIFO.");
        grade.SuggestedMarks.Should().BeInRange(3, 7);
        grade.Confidence.Should().Be("Medium");
        grade.Justification.Should().Contain("missing: queue, fifo");
    }

    [Fact]
    public void UnrelatedAnswer_Zero()
    {
        var grade = TheoryGrader.Grade(Q("Stack is LIFO, queue is FIFO."), "Photosynthesis converts light into chemical energy.");
        grade.SuggestedMarks.Should().Be(0);
        grade.Confidence.Should().Be("High");
    }

    [Fact]
    public void RubricKeywords_DriveCoverage_IncludingPhrases()
    {
        var grade = TheoryGrader.Grade(Q("Stack is LIFO, queue is FIFO.", rubric: "last in first out, FIFO"),
            "Stacks are last in first out; queues are fifo.");
        grade.Justification.Should().Contain("key terms 2/2");
    }

    [Fact]
    public void KeywordsOnly_WithoutModelAnswer_CoverageScore()
    {
        var grade = TheoryGrader.Grade(Q(null, rubric: "lifo, fifo, push, pop"), "Stack is LIFO and uses push.");
        grade.Engine.Should().Be(TheoryGrader.EngineKeywords);
        grade.SuggestedMarks.Should().Be(5);
        grade.Justification.Should().Contain("Key terms 2/4").And.Contain("missing: fifo, pop");
    }

    [Fact]
    public void NoModelAnswerAndNoKeywords_NoSuggestion_LowConfidence()
    {
        var grade = TheoryGrader.Grade(Q(null), "A stack is LIFO.");
        grade.SuggestedMarks.Should().Be(0);
        grade.Confidence.Should().Be("Low");
        grade.Justification.Should().Contain("teacher review required");
    }

    [Fact]
    public void EmptyAnswer_Zero_High() =>
        TheoryGrader.Grade(Q("Stack is LIFO."), "   ").Should().BeEquivalentTo(new { SuggestedMarks = 0.0, Confidence = "High" });

    [Fact]
    public void Plurals_AreFolded() =>
        TheoryGrader.Terms("Queues and stacks").Should().Equal("queue", "stack");

    [Fact]
    public void ParseKeywords_SplitsTrimsAndDedups() =>
        TheoryGrader.ParseKeywords(" LIFO; fifo ,\nlifo,, ").Should().Equal("lifo", "fifo");
}
