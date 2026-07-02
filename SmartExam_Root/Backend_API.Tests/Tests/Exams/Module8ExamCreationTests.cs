using System.Net;
using System.Net.Http.Json;
using Backend_API.Data;
using Backend_API.Models.Enums;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Exams;

/// <summary>
/// Module 8 — Exam Creation & Configuration.
/// Covers: instructions persistence, MCQ questions (options + correct answer),
/// reference answers, edit exam, delete exam, validation (negative cases),
/// student answer-key protection, and MCQ auto-grading on submit.
/// </summary>
public class Module8ExamCreationTests : IDisposable
{
    private readonly TestWebAppFactory _factory;
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public Module8ExamCreationTests()
    {
        _factory = new TestWebAppFactory();
        _client = _factory.CreateClient();
        _seed = _factory.SeededData;
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ── Payload builders ─────────────────────────────────────────────

    private object ValidExamPayload(string title = "Module 8 Exam", string? instructions = "Read every question carefully. No internet allowed.") => new
    {
        title,
        instructions,
        sectionId = _seed.SectionId,
        startTime = DateTime.UtcNow.AddHours(2),
        durationMinutes = 90,
        allowedApps = "[\"code.exe\"]",
        aiEvaluationEnabled = true,
        plagiarismThreshold = 70,
        questions = new object[]
        {
            new
            {
                type = "Mcq",
                bodyText = "Which data structure is LIFO?",
                marks = 5,
                testCases = Array.Empty<object>(),
                options = new[] { "Queue", "Stack", "Linked List", "Graph" },
                correctOptionIndex = 1,
                referenceAnswer = (string?)null
            },
            new
            {
                type = "Coding",
                bodyText = "Reverse an array in place.",
                marks = 20,
                testCases = new object[] { new { input = "[1,2,3]", expectedOutput = "[3,2,1]", isHidden = false } },
                options = (string[]?)null,
                correctOptionIndex = (int?)null,
                referenceAnswer = "void reverse(int[] a) { /* two-pointer swap */ }"
            },
            new
            {
                type = "Theory",
                bodyText = "Explain stack vs queue.",
                marks = 10,
                testCases = Array.Empty<object>(),
                options = (string[]?)null,
                correctOptionIndex = (int?)null,
                referenceAnswer = "Stack is LIFO, queue is FIFO."
            }
        },
        studentIds = new[] { _seed.StudentId }
    };

    // ══════════════════ POSITIVE CASES ══════════════════

    [Fact]
    public async Task CreateExam_WithInstructionsMcqAndReferenceAnswers_RoundTripsAllFields()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);

        var createResp = await _client.PostAsJsonAsync("/api/exams", ValidExamPayload());
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var examId = created.GetProperty("examId").GetString();

        var getResp = await _client.GetAsync($"/api/exams/{examId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var exam = await getResp.Content.ReadFromJsonAsync<JsonElement>();

        exam.GetProperty("instructions").GetString().Should().Contain("No internet allowed");

        var questions = exam.GetProperty("questions").EnumerateArray().ToList();
        questions.Should().HaveCount(3);

        var mcq = questions.First(q => q.GetProperty("type").GetString() == "Mcq");
        mcq.GetProperty("correctOptionIndex").GetInt32().Should().Be(1);
        var options = JsonSerializer.Deserialize<string[]>(mcq.GetProperty("optionsJson").GetString()!);
        options.Should().BeEquivalentTo(new[] { "Queue", "Stack", "Linked List", "Graph" });

        var coding = questions.First(q => q.GetProperty("type").GetString() == "Coding");
        coding.GetProperty("referenceAnswer").GetString().Should().Contain("two-pointer");
    }

    [Fact]
    public async Task StudentGetExamById_HidesAnswerKeyButShowsOptions()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var createResp = await _client.PostAsJsonAsync("/api/exams", ValidExamPayload());
        var examId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("examId").GetString();

        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var getResp = await _client.GetAsync($"/api/exams/{examId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var exam = await getResp.Content.ReadFromJsonAsync<JsonElement>();

        foreach (var q in exam.GetProperty("questions").EnumerateArray())
        {
            // Answer key + reference solution must never reach a student
            q.GetProperty("correctOptionIndex").ValueKind.Should().Be(JsonValueKind.Null);
            q.GetProperty("referenceAnswer").ValueKind.Should().Be(JsonValueKind.Null);
            if (q.GetProperty("type").GetString() == "Mcq")
            {
                // But MCQ options must still be visible so the student can answer
                q.GetProperty("optionsJson").GetString().Should().Contain("Stack");
            }
        }
    }

    [Fact]
    public async Task UpdateExam_OnScheduledExam_PersistsChanges_AndStudentSeesInstructions()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);

        // Edit the seeded exam (Scheduled): new title, instructions, MCQ question set
        var putResp = await _client.PutAsJsonAsync($"/api/exams/{_seed.ExamId}",
            ValidExamPayload(title: "Edited Seed Exam", instructions: "Updated instructions text."));
        var putBody = await putResp.Content.ReadAsStringAsync();
        putResp.StatusCode.Should().Be(HttpStatusCode.OK, because: putBody);

        var getResp = await _client.GetAsync($"/api/exams/{_seed.ExamId}");
        var exam = await getResp.Content.ReadFromJsonAsync<JsonElement>();
        exam.GetProperty("title").GetString().Should().Be("Edited Seed Exam");
        exam.GetProperty("instructions").GetString().Should().Be("Updated instructions text.");
        exam.GetProperty("questions").EnumerateArray().Should().HaveCount(3);

        // Student dashboard now carries the teacher-authored instructions + real proctor name
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var currentResp = await _client.GetAsync("/api/exams/student/current");
        currentResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = await currentResp.Content.ReadFromJsonAsync<JsonElement>();
        var data = current.GetProperty("data");
        data.GetProperty("instructions").GetString().Should().Contain("Updated instructions text.");
        data.GetProperty("proctorName").GetString().Should().Be("Test Teacher"); // real section teacher, not "Dr. Ahmed"
    }

    [Fact]
    public async Task DeleteExam_OnScheduledExam_RemovesIt()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var createResp = await _client.PostAsJsonAsync("/api/exams", ValidExamPayload(title: "Delete Me"));
        var examId = (await createResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("examId").GetString();

        var delResp = await _client.DeleteAsync($"/api/exams/{examId}");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp = await _client.GetAsync($"/api/exams/{examId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task McqAnswer_IsAutoGradedExactly_AndSkippedByPlagiarism()
    {
        // Teacher replaces the seed exam questions with the MCQ set
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var putResp = await _client.PutAsJsonAsync($"/api/exams/{_seed.ExamId}", ValidExamPayload(title: "MCQ Grading Exam"));
        putResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Find the MCQ question id
        var exam = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}")).Content.ReadFromJsonAsync<JsonElement>();
        var mcqId = exam.GetProperty("questions").EnumerateArray()
            .First(q => q.GetProperty("type").GetString() == "Mcq")
            .GetProperty("questionId").GetString();

        // Student answers the MCQ correctly (index 1 = "Stack") and submits
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString();

        var saveResp = await _client.PostAsJsonAsync($"/api/exams/sessions/{sessionId}/save-answer",
            new { questionId = mcqId, answerText = "1" });
        saveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResp = await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Teacher sees the MCQ graded at exactly full marks (5), high confidence
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var results = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}/results")).Content.ReadFromJsonAsync<JsonElement>();
        var mcqAnswer = results.EnumerateArray()
            .SelectMany(s => s.GetProperty("answers").EnumerateArray())
            .First(a => a.GetProperty("questionId").GetString() == mcqId);
        mcqAnswer.GetProperty("aiGrading").GetProperty("suggestedMarks").GetDouble().Should().Be(5);

        // And no plagiarism rows exist for the MCQ question
        var plag = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}/plagiarism")).Content.ReadFromJsonAsync<JsonElement>();
        plag.EnumerateArray().Any(p => p.GetProperty("questionId").GetString() == mcqId).Should().BeFalse();
    }

    [Fact]
    public async Task McqAnswer_WrongOption_ScoresZero()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        await _client.PutAsJsonAsync($"/api/exams/{_seed.ExamId}", ValidExamPayload(title: "MCQ Wrong Answer Exam"));
        var exam = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}")).Content.ReadFromJsonAsync<JsonElement>();
        var mcqId = exam.GetProperty("questions").EnumerateArray()
            .First(q => q.GetProperty("type").GetString() == "Mcq")
            .GetProperty("questionId").GetString();

        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var sessionResp = await _client.PostAsync($"/api/exams/{_seed.ExamId}/start-session", null);
        var sessionId = (await sessionResp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetString();
        await _client.PostAsJsonAsync($"/api/exams/sessions/{sessionId}/save-answer", new { questionId = mcqId, answerText = "0" }); // wrong (Queue)
        await _client.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);

        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var results = await (await _client.GetAsync($"/api/exams/{_seed.ExamId}/results")).Content.ReadFromJsonAsync<JsonElement>();
        var mcqAnswer = results.EnumerateArray()
            .SelectMany(s => s.GetProperty("answers").EnumerateArray())
            .First(a => a.GetProperty("questionId").GetString() == mcqId);
        mcqAnswer.GetProperty("aiGrading").GetProperty("suggestedMarks").GetDouble().Should().Be(0);
    }

    // ══════════════════ NEGATIVE CASES ══════════════════

    [Fact]
    public async Task CreateExam_WithoutQuestions_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var payload = new
        {
            title = "No Questions",
            sectionId = _seed.SectionId,
            startTime = DateTime.UtcNow.AddHours(1),
            durationMinutes = 60,
            allowedApps = "[]",
            questions = Array.Empty<object>(),
            studentIds = Array.Empty<Guid>()
        };
        var resp = await _client.PostAsJsonAsync("/api/exams", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(0)]  // no options
    [InlineData(1)]  // one option
    public async Task CreateExam_McqWithTooFewOptions_Returns400(int optionCount)
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var payload = new
        {
            title = "Bad MCQ",
            sectionId = _seed.SectionId,
            startTime = DateTime.UtcNow.AddHours(1),
            durationMinutes = 60,
            allowedApps = "[]",
            questions = new object[]
            {
                new { type = "Mcq", bodyText = "Pick one", marks = 5, testCases = Array.Empty<object>(),
                      options = Enumerable.Range(0, optionCount).Select(i => $"Opt{i}").ToArray(), correctOptionIndex = 0 }
            },
            studentIds = Array.Empty<Guid>()
        };
        var resp = await _client.PostAsJsonAsync("/api/exams", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateExam_McqWithOutOfRangeCorrectIndex_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var payload = new
        {
            title = "Bad MCQ Index",
            sectionId = _seed.SectionId,
            startTime = DateTime.UtcNow.AddHours(1),
            durationMinutes = 60,
            allowedApps = "[]",
            questions = new object[]
            {
                new { type = "Mcq", bodyText = "Pick one", marks = 5, testCases = Array.Empty<object>(),
                      options = new[] { "A", "B" }, correctOptionIndex = 5 }
            },
            studentIds = Array.Empty<Guid>()
        };
        var resp = await _client.PostAsJsonAsync("/api/exams", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateExam_McqWithTestCases_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var payload = new
        {
            title = "MCQ With TestCases",
            sectionId = _seed.SectionId,
            startTime = DateTime.UtcNow.AddHours(1),
            durationMinutes = 60,
            allowedApps = "[]",
            questions = new object[]
            {
                new { type = "Mcq", bodyText = "Pick one", marks = 5,
                      testCases = new object[] { new { input = "x", expectedOutput = "y", isHidden = false } },
                      options = new[] { "A", "B" }, correctOptionIndex = 0 }
            },
            studentIds = Array.Empty<Guid>()
        };
        var resp = await _client.PostAsJsonAsync("/api/exams", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateExam_CodingWithoutTestCases_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var payload = new
        {
            title = "Coding No TestCases",
            sectionId = _seed.SectionId,
            startTime = DateTime.UtcNow.AddHours(1),
            durationMinutes = 60,
            allowedApps = "[]",
            questions = new object[]
            {
                new { type = "Coding", bodyText = "Write code", marks = 10, testCases = Array.Empty<object>() }
            },
            studentIds = Array.Empty<Guid>()
        };
        var resp = await _client.PostAsJsonAsync("/api/exams", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateExam_PlagiarismThresholdOutOfRange_Returns400()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var payload = new
        {
            title = "Bad Threshold",
            sectionId = _seed.SectionId,
            startTime = DateTime.UtcNow.AddHours(1),
            durationMinutes = 60,
            allowedApps = "[]",
            plagiarismThreshold = 150,
            questions = new object[]
            {
                new { type = "Theory", bodyText = "Explain X", marks = 10, testCases = Array.Empty<object>() }
            },
            studentIds = Array.Empty<Guid>()
        };
        var resp = await _client.PostAsJsonAsync("/api/exams", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateExam_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var resp = await _client.PostAsJsonAsync("/api/exams", ValidExamPayload());
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateAndDelete_OnActiveExam_Return409()
    {
        // Flip the seeded exam to Active directly in the database
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var exam = await db.Exams.FindAsync(_seed.ExamId);
            exam!.Status = ExamStatus.Active;
            await db.SaveChangesAsync();
        }

        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);

        var putResp = await _client.PutAsJsonAsync($"/api/exams/{_seed.ExamId}", ValidExamPayload(title: "Should Not Save"));
        putResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var delResp = await _client.DeleteAsync($"/api/exams/{_seed.ExamId}");
        delResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateAndDelete_OnNonexistentExam_Return404()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var missing = Guid.NewGuid();

        var putResp = await _client.PutAsJsonAsync($"/api/exams/{missing}", ValidExamPayload());
        putResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var delResp = await _client.DeleteAsync($"/api/exams/{missing}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteExam_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var resp = await _client.DeleteAsync($"/api/exams/{_seed.ExamId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
