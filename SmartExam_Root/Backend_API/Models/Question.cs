using Backend_API.Models.Enums;

namespace Backend_API.Models;

public class Question
{
    public Guid QuestionId { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public QuestionType Type { get; set; }
    public string BodyText { get; set; } = string.Empty;
    public int Marks { get; set; }
    public string? ExpectedOutput { get; set; }
    public int OrderIndex { get; set; }
    public string? OptionsJson { get; set; }        // MCQ only: JSON array of option strings
    public int? CorrectOptionIndex { get; set; }    // MCQ only: index into OptionsJson (never sent to students)
    public string? ReferenceAnswer { get; set; }    // Coding/Theory: reference key / sample solution (never sent to students)

    // Navigation
    public Exam Exam { get; set; } = null!;
    public ICollection<TestCase> TestCases { get; set; } = new List<TestCase>();
    public ICollection<Answer> Answers { get; set; } = new List<Answer>();
}
