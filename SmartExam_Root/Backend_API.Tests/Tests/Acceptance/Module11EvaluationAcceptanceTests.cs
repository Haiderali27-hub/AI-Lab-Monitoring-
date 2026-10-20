using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M11 — AI-Assisted Evaluation (TEST_CASES.md §M11, layers U (via API) and I).
/// M11-U-01 → Module8ExamCreationTests.McqAnswer_IsAutoGradedExactly_AndSkippedByPlagiarism
/// M11-U-02 → Module8ExamCreationTests.McqAnswer_WrongOption_ScoresZero
/// M11-U-03…07 are pure ICodeRunner / ITheoryGrader unit tests with no API surface — not expressed here.
/// Planned contract: IMPLEMENTATION_PLAN.md §5.1 — async GradingQueue/Worker with AiGradingResult.Status,
/// per-test-case results, Docker sandbox, winnowing plagiarism, GET /api/exams/{id}/plagiarism/matrix.
/// Runner selection for tests is assumed to be config-driven: Grading:Runner=Fake, Grading:FakeDelayMs.
/// </summary>
public class Module11EvaluationAcceptanceTests : IDisposable
{
    private readonly TestHost _host = new();
    public void Dispose() => _host.Dispose();

    private async Task SubmitAnswerAsync(HttpClient student, Guid examId, Guid questionId, string text, TestHost? host = null)
    {
        host ??= _host;
        var sid = await host.StartSessionAsync(student, examId);
        (await TestHost.SaveAnswerAsync(student, sid, questionId, text)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<List<JsonElement>> PlagiarismAsync(Guid examId) =>
        (await (await (await _host.AsTeacher()).GetAsync($"/api/exams/{examId}/plagiarism")).Content.ReadFromJsonAsync<List<JsonElement>>())!;

    private async Task<double> PairSimilarityAsync(string a, string b)
    {
        // Threshold 0 so every pair is reported with its score.
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId, _host.Seed.Student2Id },
            extra: new Dictionary<string, object?> { ["plagiarismThreshold"] = 0 },
            questions: new[] { TestHost.CodingQuestion() });
        var qid = (await _host.QuestionIdsAsync(examId)).Single();
        await SubmitAnswerAsync(await _host.AsStudent(), examId, qid, a);
        await SubmitAnswerAsync(await _host.AsStudent2(), examId, qid, b);
        var pairs = await PlagiarismAsync(examId);
        pairs.Should().ContainSingle();
        return pairs[0].GetProperty("similarityScore").GetDouble();
    }

    private const string SumLoop = """
        int sum(int* arr, int n) {
            int total = 0;
            for (int i = 0; i < n; i++) {
                total += arr[i];
            }
            return total;
        }
        """;

    // ── U rows expressed through the API (plagiarism engine) ─────────────────

    [Fact(Skip = "Pending: M11-U-08 — token-normalised (identifier-insensitive) plagiarism")]
    public async Task M11_U_08_Plagiarism_IgnoresIdentifierRenames_AtLeast90()
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
        (await PairSimilarityAsync(SumLoop, renamed)).Should().BeGreaterThanOrEqualTo(90);
    }

    [Fact(Skip = "Pending: M11-U-09 — comment/whitespace-insensitive plagiarism")]
    public async Task M11_U_09_Plagiarism_IgnoresCommentsAndWhitespace_AtLeast95()
    {
        const string commented = """
            // sums the array
            int sum(int* arr,int n){ /* running total */ int total=0;
              for(int i=0;i<n;i++){total+=arr[i];}   // add each element
              return total; }
            """;
        (await PairSimilarityAsync(SumLoop, commented)).Should().BeGreaterThanOrEqualTo(95);
    }

    [Fact(Skip = "Pending: M11-U-10 — winnowing plagiarism keeps distinct solutions apart")]
    public async Task M11_U_10_DifferentAlgorithms_LowSimilarity_Under30()
    {
        const string recursive = """
            int sum(int* arr, int n) {
                if (n == 0) return 0;
                return arr[n - 1] + sum(arr, n - 1);
            }
            """;
        (await PairSimilarityAsync(SumLoop, recursive)).Should().BeLessThan(30);
    }

    // ── I rows ───────────────────────────────────────────────────────────────

    [Fact(Skip = "Pending: M11-I-01 — asynchronous grading queue")]
    public async Task M11_I_01_SubmitReturnsBeforeGradingFinishes_StatusPending()
    {
        using var host = new TestHost(b => { b.UseSetting("Grading:Runner", "Fake"); b.UseSetting("Grading:FakeDelayMs", "5000"); });
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, host.Seed.Question1Id, SumLoop)).StatusCode.Should().Be(HttpStatusCode.OK);

        var sw = Stopwatch.StartNew();
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));

        var results = await (await (await host.AsTeacher()).GetAsync($"/api/exams/{host.Seed.ExamId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>();
        results!.Single().GetProperty("answers")[0].GetProperty("aiGrading").GetProperty("status").GetString().Should().Be("Pending");
    }

    [Fact(Skip = "Pending: M11-I-02 — grading worker with per-test-case results")]
    public async Task M11_I_02_WorkerGradesPendingAnswers_WithPerCaseResults()
    {
        using var host = new TestHost(b => { b.UseSetting("Grading:Runner", "Fake"); b.UseSetting("Grading:FakeDelayMs", "100"); });
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, host.Seed.Question1Id, SumLoop)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var teacher = await host.AsTeacher();
        JsonElement grading = default;
        await TestHost.Eventually(async () =>
        {
            var results = await (await teacher.GetAsync($"/api/exams/{host.Seed.ExamId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>();
            grading = results!.Single().GetProperty("answers")[0].GetProperty("aiGrading");
            return grading.GetProperty("status").GetString() == "Graded";
        }, TimeSpan.FromSeconds(15), "the worker must drain the grading queue");

        grading.GetProperty("testCaseResults").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact(Skip = "Pending: M11-I-03 — hidden test-case results not exposed to students")]
    public async Task M11_I_03_StudentResult_DetailsOnlyVisibleTestCases()
    {
        const string hiddenExpected = "HIDDEN-EXPECTED-424242";
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId },
            questions: new[] { TestHost.CodingQuestion(20, ("1 2", "3", false), ("40 2", hiddenExpected, true)) });
        var qid = (await _host.QuestionIdsAsync(examId)).Single();
        await SubmitAnswerAsync(await _host.AsStudent(), examId, qid, SumLoop);

        var teacher = await _host.AsTeacher();
        (await teacher.PostAsync($"/api/exams/{examId}/force-submit-all", null)).StatusCode.Should().Be(HttpStatusCode.OK); // ends exam → results visible

        var resp = await (await _host.AsStudent()).GetAsync($"/api/student/exams/{examId}/result");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var raw = await resp.Content.ReadAsStringAsync();
        raw.Should().NotContain(hiddenExpected);
        var answer = JsonDocument.Parse(raw).RootElement.GetProperty("answers")[0];
        answer.GetProperty("aiFeedback").GetProperty("testCaseResults").GetArrayLength().Should().Be(1);

        // The teacher does see both.
        (await (await teacher.GetAsync($"/api/exams/{examId}/results")).Content.ReadAsStringAsync()).Should().Contain(hiddenExpected);
    }

    [Fact(Skip = "Pending: M11-I-04 — Docker sandbox (no network)")]
    public async Task M11_I_04_Sandbox_HasNoNetwork()
    {
        using var host = new TestHost(b => b.UseSetting("Grading:Runner", "Docker"));
        const string socketCode = """
            #include <sys/socket.h>
            #include <netinet/in.h>
            #include <arpa/inet.h>
            #include <stdio.h>
            int main(){ int s=socket(AF_INET,SOCK_STREAM,0); struct sockaddr_in a={0}; a.sin_family=AF_INET; a.sin_port=htons(80);
              inet_pton(AF_INET,"1.1.1.1",&a.sin_addr); if(connect(s,(struct sockaddr*)&a,sizeof a)==0) printf("3"); else printf("NO-NET"); }
            """;
        var examId = await host.CreateExamAsync(new[] { host.Seed.StudentId }, questions: new[] { TestHost.CodingQuestion(10, ("1 2", "3", false)) });
        var qid = (await host.QuestionIdsAsync(examId)).Single();
        await SubmitAnswerAsync(await host.AsStudent(), examId, qid, socketCode, host);

        var teacher = await host.AsTeacher();
        await TestHost.Eventually(async () =>
            (await (await teacher.GetAsync($"/api/exams/{examId}/results")).Content.ReadAsStringAsync()).Contains("\"Graded\""),
            TimeSpan.FromSeconds(60), "sandboxed run must finish");
        var grading = (await (await teacher.GetAsync($"/api/exams/{examId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>())!
            .Single().GetProperty("answers")[0].GetProperty("aiGrading");
        grading.GetProperty("suggestedMarks").GetDouble().Should().Be(0, "the connect() must fail inside --network none");
    }

    [Fact(Skip = "Pending: M11-I-05 — Docker sandbox (memory limit)")]
    public async Task M11_I_05_Sandbox_MemoryLimit_KillsAndFailsCase()
    {
        using var host = new TestHost(b => b.UseSetting("Grading:Runner", "Docker"));
        const string hog = """
            #include <stdlib.h>
            #include <string.h>
            #include <stdio.h>
            int main(){ char* p = malloc(1024L*1024*1024); memset(p, 1, 1024L*1024*1024); printf("3"); }
            """;
        var examId = await host.CreateExamAsync(new[] { host.Seed.StudentId }, questions: new[] { TestHost.CodingQuestion(10, ("1 2", "3", false)) });
        var qid = (await host.QuestionIdsAsync(examId)).Single();
        await SubmitAnswerAsync(await host.AsStudent(), examId, qid, hog, host);

        var teacher = await host.AsTeacher();
        await TestHost.Eventually(async () =>
            (await (await teacher.GetAsync($"/api/exams/{examId}/results")).Content.ReadAsStringAsync()).Contains("\"Graded\""),
            TimeSpan.FromSeconds(60), "sandboxed run must finish");
        var grading = (await (await teacher.GetAsync($"/api/exams/{examId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>())!
            .Single().GetProperty("answers")[0].GetProperty("aiGrading");
        grading.GetProperty("suggestedMarks").GetDouble().Should().Be(0);
        grading.GetProperty("justification").GetString().Should().ContainEquivalentOf("memory");
    }

    [Fact(Skip = "Pending: M11-I-06 — pairwise plagiarism matrix")]
    public async Task M11_I_06_PlagiarismMatrix_3Students_Is3x3Symmetric()
    {
        var (thirdId, thirdEmail, thirdPassword) = await _host.CreateUserAsync("Student");
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId, _host.Seed.Student2Id, thirdId },
            questions: new[] { TestHost.CodingQuestion() });
        var qid = (await _host.QuestionIdsAsync(examId)).Single();
        await SubmitAnswerAsync(await _host.AsStudent(), examId, qid, SumLoop);
        await SubmitAnswerAsync(await _host.AsStudent2(), examId, qid, SumLoop);
        await SubmitAnswerAsync(await _host.LoginAsync(thirdEmail, thirdPassword), examId, qid, "print(sum(map(int, input().split())))");

        var resp = await (await _host.AsTeacher()).GetAsync($"/api/exams/{examId}/plagiarism/matrix");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await TestHost.DataAsync(resp);

        body.GetProperty("students").GetArrayLength().Should().Be(3);
        var m = body.GetProperty("matrix").EnumerateArray().Select(r => r.EnumerateArray().Select(c => c.GetDouble()).ToArray()).ToArray();
        m.Should().HaveCount(3).And.OnlyContain(r => r.Length == 3);
        for (var i = 0; i < 3; i++)
            for (var j = 0; j < 3; j++)
                m[i][j].Should().BeApproximately(m[j][i], 0.01, $"matrix[{i},{j}] must equal matrix[{j},{i}]");
        m[0][1].Should().BeGreaterThan(90, "students 1 and 2 submitted identical code");
    }

    [Fact(Skip = "Pending: M11-I-07 — matched segments with line ranges")]
    public async Task M11_I_07_PlagiarismPair_HasMatchedSegmentLineRanges()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId, _host.Seed.Student2Id },
            questions: new[] { TestHost.CodingQuestion() });
        var qid = (await _host.QuestionIdsAsync(examId)).Single();
        await SubmitAnswerAsync(await _host.AsStudent(), examId, qid, SumLoop);
        await SubmitAnswerAsync(await _host.AsStudent2(), examId, qid, SumLoop);

        var pair = (await PlagiarismAsync(examId)).Single();
        var segments = JsonDocument.Parse(pair.GetProperty("matchingSegments").GetString()!).RootElement;
        segments.GetArrayLength().Should().BeGreaterThan(0);
        foreach (var seg in segments.EnumerateArray())
        {
            var aStart = seg.GetProperty("aStartLine").GetInt32();
            var aEnd = seg.GetProperty("aEndLine").GetInt32();
            aStart.Should().BeGreaterThan(0);
            aEnd.Should().BeGreaterThanOrEqualTo(aStart);
            seg.GetProperty("bEndLine").GetInt32().Should().BeGreaterThanOrEqualTo(seg.GetProperty("bStartLine").GetInt32());
        }
    }

    [Fact]
    public async Task M11_I_08_TeacherOverrideWins_OverAiSuggestion()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "A stack is LIFO and a queue is FIFO.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var teacher = await _host.AsTeacher();
        var answerId = await _host.WithDbAsync(db => db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerId).SingleAsync());
        var aiMarks = await _host.WithDbAsync(db => db.AiGradingResults.Where(g => g.AnswerId == answerId).Select(g => g.SuggestedMarks).SingleAsync());
        aiMarks.Should().NotBe(3.5, "precondition: the override must differ from the AI suggestion");

        (await teacher.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 3.5, note = "Missing examples" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        // Overriding twice updates in place rather than adding a second override.
        (await teacher.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 4.0, note = "Re-checked" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // Teacher view
        var results = await (await teacher.GetAsync($"/api/exams/{_host.Seed.ExamId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>();
        var ans = results!.Single(r => r.GetProperty("sessionId").GetGuid() == sid).GetProperty("answers")[0];
        ans.GetProperty("teacherOverride").GetProperty("finalMarks").GetDouble().Should().Be(4.0);
        ans.GetProperty("aiGrading").GetProperty("suggestedMarks").GetDouble().Should().Be(aiMarks, "the AI suggestion is kept for audit");

        // Student view, after the exam ends: the final mark is the override.
        (await teacher.PostAsync($"/api/exams/{_host.Seed.ExamId}/force-submit-all", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await (await student.GetAsync($"/api/student/exams/{_host.Seed.ExamId}/result")).Content.ReadFromJsonAsync<JsonElement>();
        var studentAnswer = result.GetProperty("answers")[0];
        studentAnswer.GetProperty("earnedMarks").GetDouble().Should().Be(4.0);
        studentAnswer.GetProperty("teacherOverridden").GetBoolean().Should().BeTrue();
        result.GetProperty("earnedMarks").GetDouble().Should().Be(4.0);
    }
}
