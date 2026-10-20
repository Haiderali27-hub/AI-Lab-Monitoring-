using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Backend_API.Models;
using Backend_API.Services.Grading;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// M11 — AI-Assisted Evaluation (TEST_CASES.md §M11, layers U (via API) and I).
/// M11-U-01 → Module8ExamCreationTests.McqAnswer_IsAutoGradedExactly_AndSkippedByPlagiarism
/// M11-U-02 → Module8ExamCreationTests.McqAnswer_WrongOption_ScoresZero
/// M11-U-03…07 are pure unit tests in Tests/Grading (TestCaseScorerTests, OutputComparerTests, TheoryGraderTests).
/// Contract: IMPLEMENTATION_PLAN.md §5.1 — async GradingQueue/Worker with AiGradingResult.Status, per-test-case
/// results, Docker sandbox, winnowing plagiarism, GET /api/exams/{id}/plagiarism/matrix. The default test host uses
/// Grading:Runner=Fake (FakeDelayMs 0); M11-I-04/05 use the real Docker sandbox and skip without Docker + gcc:13.
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [Fact]
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

    [DockerFact]
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

    [DockerFact]
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

    [Fact]
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

    [Fact]
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

    // ── Stage S7 additions: endpoints, access, audit, worker behaviour ──────

    private async Task<JsonElement> GradingOfAsync(Guid examId, Guid sessionId, Guid questionId, TestHost? host = null, bool waitGraded = true)
    {
        host ??= _host;
        var teacher = await host.AsTeacher();
        JsonElement grading = default;
        await TestHost.Eventually(async () =>
        {
            var results = await (await teacher.GetAsync($"/api/exams/{examId}/results")).Content.ReadFromJsonAsync<List<JsonElement>>();
            var answer = results!.Single(r => r.GetProperty("sessionId").GetGuid() == sessionId).GetProperty("answers").EnumerateArray()
                .Single(a => a.GetProperty("questionId").GetGuid() == questionId);
            grading = answer.GetProperty("aiGrading");
            return !waitGraded || grading.GetProperty("status").GetString() != AiGradingResult.StatusPending;
        }, TimeSpan.FromSeconds(15), "the grading worker must finish");
        return grading;
    }

    private static object CodingWith(params object[] testCases) => new
    {
        type = "Coding", bodyText = "Print the sum of two integers.", marks = 20, testCases,
        options = (string[]?)null, correctOptionIndex = (int?)null, referenceAnswer = (string?)null, language = "cpp"
    };

    [Fact]
    public async Task M11_I_09_ResultsPlagiarismOverrideAndMatrix_AreOwnerOrAdminOnly()
    {
        // D-13: another section's teacher could read every exam's results and change any grade.
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "A stack is LIFO.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        var answerId = await _host.WithDbAsync(db => db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerId).SingleAsync());

        var (_, email, password) = await _host.CreateUserAsync("Teacher");
        var other = await _host.LoginAsync(email, password);
        var exam = _host.Seed.ExamId;
        (await other.GetAsync($"/api/exams/{exam}/results")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync($"/api/exams/{exam}/plagiarism")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.GetAsync($"/api/exams/{exam}/plagiarism/matrix")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 1, note = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PostAsync($"/api/exams/{exam}/grading/accept-ai", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PostAsync($"/api/answers/{answerId}/regrade", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.GetAsync($"/api/exams/{exam}/plagiarism/matrix")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // The owner and an admin may; an unknown exam is 404.
        (await (await _host.AsTeacher()).GetAsync($"/api/exams/{exam}/results")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _host.AsAdmin()).GetAsync($"/api/exams/{exam}/plagiarism/matrix")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await _host.AsTeacher()).GetAsync($"/api/exams/{Guid.NewGuid()}/results")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _host.WithDbAsync(db => db.TeacherGradeOverrides.CountAsync())).Should().Be(0);
    }

    [Fact]
    public async Task M11_I_10_Matrix_MockExamIs400_AndQuestionFilterIsValidated()
    {
        var mock = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, extra: new Dictionary<string, object?> { ["isMock"] = true });
        var teacher = await _host.AsTeacher();
        (await teacher.GetAsync($"/api/exams/{mock}/plagiarism/matrix")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId, _host.Seed.Student2Id }, questions: new[] { TestHost.CodingQuestion(), TestHost.TheoryQuestion() });
        var qids = await _host.QuestionIdsAsync(examId);
        await SubmitAnswerAsync(await _host.AsStudent(), examId, qids[1], "Stack is LIFO and queue is FIFO in every textbook.");
        await SubmitAnswerAsync(await _host.AsStudent2(), examId, qids[1], "Stack is LIFO and queue is FIFO in every textbook.");

        (await teacher.GetAsync($"/api/exams/{examId}/plagiarism/matrix?questionId={Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var byTheory = await TestHost.DataAsync(await teacher.GetAsync($"/api/exams/{examId}/plagiarism/matrix?questionId={qids[1]}"));
        byTheory.GetProperty("matrix")[0][1].GetDouble().Should().Be(100);
        byTheory.GetProperty("questions").GetArrayLength().Should().Be(1);
        var byCoding = await TestHost.DataAsync(await teacher.GetAsync($"/api/exams/{examId}/plagiarism/matrix?questionId={qids[0]}"));
        byCoding.GetProperty("matrix")[0][1].GetDouble().Should().Be(0, "nobody answered the coding question");
        byCoding.GetProperty("matrix")[1][1].GetDouble().Should().Be(100);
    }

    [Fact]
    public async Task M11_I_11_AcceptAiSuggestions_CreatesOverrides_Audited()
    {
        // Both questions can be graded automatically: test cases (coding) and a model answer (theory). Answers the
        // grader could not mark are left for a manual mark (M11-I-22).
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId },
            questions: new[] { TestHost.CodingQuestion(), TestHost.TheoryQuestion() });
        var qids = await _host.QuestionIdsAsync(examId);
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, examId);
        (await TestHost.SaveAnswerAsync(student, sid, qids[0], "int main(){ return 0; }")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SaveAnswerAsync(student, sid, qids[1], "A stack is LIFO.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        await GradingOfAsync(examId, sid, qids[0]);

        var teacher = await _host.AsTeacher();
        var resp = await teacher.PostAsync($"/api/exams/{examId}/grading/accept-ai", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.JsonAsync(resp)).GetProperty("accepted").GetInt32().Should().Be(2);

        var overrides = await _host.WithDbAsync(db => db.TeacherGradeOverrides.Include(o => o.Answer).ThenInclude(a => a.AiGradingResult).ToListAsync());
        overrides.Should().HaveCount(2).And.OnlyContain(o => o.FinalMarks == o.Answer.AiGradingResult!.SuggestedMarks && o.Note == "Accepted AI suggestion");
        (await _host.AuditLogsAsync("AI_SUGGESTIONS_ACCEPTED")).Should().ContainSingle();

        // Answers that already have an override are left alone.
        (await TestHost.JsonAsync(await teacher.PostAsync($"/api/exams/{examId}/grading/accept-ai", null)))
            .GetProperty("accepted").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task M11_I_12_Regrade_RequeuesAndGradesAgain_Audited()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId },
            questions: new[] { TestHost.CodingQuestion(20, ("1 2", "3", false)), TestHost.TheoryQuestion() });
        var qids = await _host.QuestionIdsAsync(examId);
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, examId);
        (await TestHost.SaveAnswerAsync(student, sid, qids[0], "int main(){ /* #fail */ }")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GradingOfAsync(examId, sid, qids[0])).GetProperty("suggestedMarks").GetDouble().Should().Be(0);

        var answerId = await _host.WithDbAsync(db => db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerId).SingleAsync());
        var teacher = await _host.AsTeacher();
        var resp = await teacher.PostAsync($"/api/answers/{answerId}/regrade", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        (await TestHost.JsonAsync(resp)).GetProperty("status").GetString().Should().Be("Pending");
        (await _host.AuditLogsAsync("AI_REGRADE_REQUESTED")).Should().ContainSingle().Which.GetProperty("entityId").GetString().Should().Be(answerId.ToString());

        var again = await GradingOfAsync(examId, sid, qids[0]);
        again.GetProperty("status").GetString().Should().Be("Graded");
        again.GetProperty("testCaseResults").GetArrayLength().Should().Be(1);
        (await _host.WithDbAsync(db => db.AiGradingResults.Where(g => g.AnswerId == answerId).Select(g => g.QueueVersion).SingleAsync())).Should().Be(1);
    }

    [Fact]
    public async Task M11_I_13_Regrade_RefusesMcqAndRunningSessions()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "LIFO")).StatusCode.Should().Be(HttpStatusCode.OK);
        var answerId = await _host.WithDbAsync(db => db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerId).SingleAsync());
        var teacher = await _host.AsTeacher();
        (await teacher.PostAsync($"/api/answers/{answerId}/regrade", null)).StatusCode.Should().Be(HttpStatusCode.Conflict, "the session is still running");
        (await teacher.PostAsync($"/api/answers/{Guid.NewGuid()}/regrade", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var mcqExam = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, questions: new object[]
        {
            new { type = "Mcq", bodyText = "Pick B", marks = 2, testCases = Array.Empty<object>(), options = new[] { "A", "B" }, correctOptionIndex = 1, referenceAnswer = (string?)null }
        });
        var mcqQ = (await _host.QuestionIdsAsync(mcqExam)).Single();
        await SubmitAnswerAsync(student, mcqExam, mcqQ, "1");
        var mcqAnswer = await _host.WithDbAsync(db => db.Answers.Where(a => a.QuestionId == mcqQ).Select(a => a.AnswerId).SingleAsync());
        (await teacher.PostAsync($"/api/answers/{mcqAnswer}/regrade", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task M11_I_14_CompileError_ZeroHighConfidence_StudentSeesHiddenTallyOnly()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId },
            questions: new[] { TestHost.CodingQuestion(20, ("1 2", "3", false), ("2 2", "4", true), ("5 5", "10", true)) });
        var qid = (await _host.QuestionIdsAsync(examId)).Single();
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, examId);
        (await TestHost.SaveAnswerAsync(student, sid, qid, "int main(){ #compile-error }")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var grading = await GradingOfAsync(examId, sid, qid);
        grading.GetProperty("suggestedMarks").GetDouble().Should().Be(0);
        grading.GetProperty("confidence").GetString().Should().Be("High");
        grading.GetProperty("justification").GetString().Should().Contain("Compilation failed");
        grading.GetProperty("compileOutput").GetString().Should().Contain("error");
        grading.GetProperty("engine").GetString().Should().Be(FakeCodeRunner.EngineName);
        grading.GetProperty("testCaseResults").EnumerateArray().Should().OnlyContain(r => r.GetProperty("status").GetString() == "NotRun");
        grading.GetProperty("analysis").ValueKind.Should().Be(JsonValueKind.Object);

        (await (await _host.AsTeacher()).PostAsync($"/api/exams/{examId}/force-submit-all", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await (await student.GetAsync($"/api/student/exams/{examId}/result")).Content.ReadFromJsonAsync<JsonElement>();
        var feedback = result.GetProperty("answers")[0].GetProperty("aiFeedback");
        feedback.GetProperty("testCaseResults").GetArrayLength().Should().Be(1);
        feedback.GetProperty("hiddenTotal").GetInt32().Should().Be(2);
        feedback.GetProperty("hiddenPassed").GetInt32().Should().Be(0);
        feedback.TryGetProperty("analysis", out _).Should().BeFalse("static-analysis flags are for staff");
    }

    [Fact]
    public async Task M11_I_15_GradingCompleted_IsBroadcastToTheExamGroup()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question1Id, "int main(){}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        await TestHost.Eventually(() => Task.FromResult(_host.Hub.Named("GradingCompleted").Any()), TimeSpan.FromSeconds(15), "the worker announces the grade");
        var msg = _host.Hub.Named("GradingCompleted").Single();
        msg.Target.Should().Be($"exam:{_host.Seed.ExamId}");
        msg.ArgsJson.Should().Contain(sid.ToString()).And.Contain("Graded");
    }

    [Fact]
    public async Task M11_I_16_Worker_RequeuesPendingRowsOnStart()
    {
        // A row left Pending by a restart (the queue lives in memory) is graded when a worker starts.
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question1Id, "int main(){}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var answerId = await _host.WithDbAsync(db => db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerId).SingleAsync());
        await _host.WithDbAsync(async db =>
        {
            db.AiGradingResults.Add(new AiGradingResult { AnswerId = answerId, Status = AiGradingResult.StatusPending, Confidence = "Low", Justification = "Queued" });
            await db.SaveChangesAsync();
        });

        using var worker = ActivatorUtilities.CreateInstance<GradingWorker>(_host.Services);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await TestHost.Eventually(() => _host.WithDbAsync(db =>
                db.AiGradingResults.AnyAsync(g => g.AnswerId == answerId && g.Status == AiGradingResult.StatusGraded)),
                TimeSpan.FromSeconds(15), "the pending row is re-queued and graded");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task M11_I_17_Override_IsValidated_AndAudited()
    {
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, _host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, _host.Seed.Question2Id, "LIFO")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        var answerId = await _host.WithDbAsync(db => db.Answers.Where(a => a.SessionId == sid).Select(a => a.AnswerId).SingleAsync());

        var teacher = await _host.AsTeacher();
        (await teacher.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 11, note = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "Q2 is worth 10");
        (await teacher.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = -1, note = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await teacher.PostAsJsonAsync($"/api/answers/{answerId}/override", new { finalMarks = 6.5, note = "Good" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var log = (await _host.AuditLogsAsync("GRADE_OVERRIDDEN")).Should().ContainSingle().Subject;
        log.GetProperty("entityId").GetString().Should().Be(answerId.ToString());
        log.GetRawText().Should().Contain("6.5");
    }

    [Fact]
    public async Task M11_I_18_TestCaseSettings_AreValidated_StoredAndHiddenFromStudents()
    {
        var teacher = await _host.AsTeacher();
        async Task<HttpStatusCode> CreateWith(object tc, string? language = "cpp")
        {
            var payload = new Dictionary<string, object?>
            {
                ["title"] = "Settings exam", ["sectionId"] = _host.Seed.SectionId, ["startTime"] = DateTime.UtcNow.AddMinutes(-1),
                ["durationMinutes"] = 60, ["allowedApps"] = "[]", ["studentIds"] = new[] { _host.Seed.StudentId },
                ["questions"] = new object[]
                {
                    new { type = "Coding", bodyText = "Sum", marks = 10, testCases = new[] { tc }, language, options = (string[]?)null, correctOptionIndex = (int?)null }
                }
            };
            return (await teacher.PostAsJsonAsync("/api/exams", payload)).StatusCode;
        }

        (await CreateWith(new { input = "1", expectedOutput = "1", isHidden = false, weight = 0 })).Should().Be(HttpStatusCode.BadRequest);
        (await CreateWith(new { input = "1", expectedOutput = "1", isHidden = false, timeLimitMs = 50 })).Should().Be(HttpStatusCode.BadRequest);
        (await CreateWith(new { input = "1", expectedOutput = "1", isHidden = false, floatTolerance = -0.1 })).Should().Be(HttpStatusCode.BadRequest);
        (await CreateWith(new { input = "1", expectedOutput = "1", isHidden = false }, language: "java")).Should().Be(HttpStatusCode.BadRequest);

        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, questions: new[]
        {
            CodingWith(
                new { input = "1 2", expectedOutput = "3", isHidden = false, weight = 2, timeLimitMs = 1500, isEdgeCase = false, category = "basic", ignoreCase = true, floatTolerance = 0.01 },
                new { input = "0 0", expectedOutput = "0", isHidden = true, weight = 1, isEdgeCase = true, category = "edge" })
        });
        var detail = await TestHost.JsonAsync(await teacher.GetAsync($"/api/exams/{examId}"));
        var q = detail.GetProperty("questions")[0];
        q.GetProperty("language").GetString().Should().Be("cpp");
        var tcs = q.GetProperty("testCases");
        tcs.GetArrayLength().Should().Be(2);
        tcs[0].GetProperty("weight").GetDouble().Should().Be(2);
        tcs[0].GetProperty("timeLimitMs").GetInt32().Should().Be(1500);
        tcs[0].GetProperty("ignoreCase").GetBoolean().Should().BeTrue();
        tcs[0].GetProperty("floatTolerance").GetDouble().Should().Be(0.01);
        tcs[1].GetProperty("isEdgeCase").GetBoolean().Should().BeTrue();

        var studentView = await TestHost.JsonAsync(await (await _host.AsStudent()).GetAsync($"/api/exams/{examId}"));
        studentView.GetProperty("questions")[0].GetProperty("testCases").GetArrayLength().Should().Be(1, "hidden cases never reach students");
    }

    [Fact]
    public async Task M11_I_19_WeightedCases_AndTheoryTfIdf_ThroughTheApi()
    {
        var examId = await _host.CreateExamAsync(new[] { _host.Seed.StudentId }, questions: new object[]
        {
            CodingWith(new { input = "1 2", expectedOutput = "3", isHidden = false, weight = 3 }, new { input = "2 2", expectedOutput = "4", isHidden = true, weight = 1 }),
            TestHost.TheoryQuestion()
        });
        var qids = await _host.QuestionIdsAsync(examId);
        var student = await _host.AsStudent();
        var sid = await _host.StartSessionAsync(student, examId);
        (await TestHost.SaveAnswerAsync(student, sid, qids[0], "int main(){}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SaveAnswerAsync(student, sid, qids[1], "A stack is LIFO and a queue is FIFO.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var coding = await GradingOfAsync(examId, sid, qids[0]);
        coding.GetProperty("suggestedMarks").GetDouble().Should().Be(20);
        coding.GetProperty("testCaseResults").EnumerateArray().Select(r => r.GetProperty("weight").GetDouble()).Should().Equal(3, 1);

        var theory = await GradingOfAsync(examId, sid, qids[1]);
        theory.GetProperty("engine").GetString().Should().Be(TheoryGrader.EngineTfIdf);
        theory.GetProperty("suggestedMarks").GetDouble().Should().Be(10);
        theory.GetProperty("justification").GetString().Should().Contain("Similarity to the model answer");
    }

    [Fact]
    public async Task M11_I_20_RunnerNone_LeavesCodingForTheTeacher_WithoutQueueing()
    {
        using var host = new TestHost(b => b.UseSetting("Grading:Runner", "None"));
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, host.Seed.Question1Id, "int main(){}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var grading = await GradingOfAsync(host.Seed.ExamId, sid, host.Seed.Question1Id, host, waitGraded: false);
        grading.GetProperty("status").GetString().Should().Be("Graded");
        grading.GetProperty("confidence").GetString().Should().Be("Low");
        grading.GetProperty("justification").GetString().Should().Contain("Not compiled").And.Contain("teacher review required");
    }

    [Fact]
    public async Task M11_I_21_EngineFailure_EndsAsFailed_NotAsAFakeGrade()
    {
        using var host = new TestHost(b =>
        {
            b.UseSetting("Grading:Runner", "Docker");
            b.UseSetting("Grading:Docker:Executable", "smartexam-no-such-docker");
            b.UseSetting("Grading:MaxAttempts", "1");
        });
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, host.Seed.Question1Id, "int main(){}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var grading = await GradingOfAsync(host.Seed.ExamId, sid, host.Seed.Question1Id, host);
        grading.GetProperty("status").GetString().Should().Be("Failed");
        grading.GetProperty("suggestedMarks").GetDouble().Should().Be(0);
        grading.GetProperty("confidence").GetString().Should().Be("Low");
        grading.GetProperty("justification").GetString().Should().Contain("Automatic grading failed").And.Contain("teacher review required");
        grading.GetProperty("testCaseResults").EnumerateArray().Should().OnlyContain(r => r.GetProperty("status").GetString() == "NotRun");
    }

    // ── S7 verifier regressions ──────────────────────────────────────────────

    [Fact]
    public async Task M11_I_22_AcceptAi_LeavesAnswersTheGraderCouldNotMark_ForAManualMark()
    {
        // Runner None: the coding answer's "0, Low, Not compiled — teacher review required" is not a suggestion.
        using var host = new TestHost(b => b.UseSetting("Grading:Runner", "None"));
        var examId = await host.CreateExamAsync(new[] { host.Seed.StudentId },
            questions: new[] { TestHost.CodingQuestion(), TestHost.TheoryQuestion() });
        var qids = await host.QuestionIdsAsync(examId);
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, examId);
        (await TestHost.SaveAnswerAsync(student, sid, qids[0], "int main(){ return 0; }")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SaveAnswerAsync(student, sid, qids[1], "A stack is LIFO, a queue is FIFO.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var teacher = await host.AsTeacher();
        var body = await TestHost.JsonAsync(await teacher.PostAsync($"/api/exams/{examId}/grading/accept-ai", null));
        body.GetProperty("accepted").GetInt32().Should().Be(1, "only the theory answer has a real suggestion");
        body.GetProperty("needsManualMark").GetInt32().Should().Be(1);
        body.GetProperty("message").GetString().Should().Contain("could not be graded automatically");

        var overridden = await host.WithDbAsync(db => db.TeacherGradeOverrides.Select(o => o.Answer.QuestionId).ToListAsync());
        overridden.Should().Equal(qids[1]);
    }

    private sealed class StubLlmHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
            });
    }

    /// <summary>A host with the LLM switched on (key in a test-only environment variable) and a canned provider reply.</summary>
    private static TestHost LlmHost(string replyBody, string? model = null)
    {
        var keyEnv = "SMARTEXAM_TEST_LLM_KEY_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(keyEnv, "test-key");
        return new TestHost(b =>
        {
            b.UseSetting("Grading:Llm:Enabled", "true");
            b.UseSetting("Grading:Llm:ApiKeyEnv", keyEnv);
            b.UseSetting("Grading:Llm:MaxAttempts", "1");
            if (model is not null) b.UseSetting("Grading:Llm:Model", model);
            b.ConfigureTestServices(s => s.AddHttpClient(LlmClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => new StubLlmHandler(replyBody)));
        });
    }

    private async Task<JsonElement> GradeTheoryOnAsync(TestHost host, string answer)
    {
        var examId = await host.CreateExamAsync(new[] { host.Seed.StudentId }, questions: new[] { TestHost.TheoryQuestion() });
        var qid = (await host.QuestionIdsAsync(examId)).Single();
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, examId);
        (await TestHost.SaveAnswerAsync(student, sid, qid, answer)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);
        return await GradingOfAsync(examId, sid, qid, host);
    }

    [Fact]
    public async Task M11_I_23_LlmReplyOfAnUnexpectedShape_FallsBackToTheOfflineGrade()
    {
        // "content" is a number, not a string: JsonNode throws InvalidOperationException, which used to escape the
        // client and leave the answer Pending for ever.
        using var host = LlmHost("""{"choices":[{"message":{"content":42}}]}""");
        var grading = await GradeTheoryOnAsync(host, "A stack is LIFO, a queue is FIFO.");
        grading.GetProperty("status").GetString().Should().Be("Graded");
        grading.GetProperty("engine").GetString().Should().Be(TheoryGrader.EngineTfIdf);
    }

    [Fact]
    public async Task M11_I_24_LongEngineLabel_IsCutToTheColumnLength()
    {
        // Engine is varchar(64): a long configured model name must not make Postgres refuse the grade.
        var model = "org/" + new string('m', 120);
        using var host = LlmHost(
            """{"choices":[{"message":{"content":"{\"marks\": 8, \"justification\": \"Mostly right.\", \"confidence\": \"medium\"}"}}]}""",
            model);
        var grading = await GradeTheoryOnAsync(host, "A stack is LIFO, a queue is FIFO.");
        grading.GetProperty("status").GetString().Should().Be("Graded");
        grading.GetProperty("suggestedMarks").GetDouble().Should().Be(8);
        var engine = grading.GetProperty("engine").GetString()!;
        engine.Should().StartWith("LLM (");
        engine.Length.Should().BeLessThanOrEqualTo(GradingService.MaxEngineLength);
    }

    /// <summary>Refuses the first save of a graded fake-runner result, as Postgres would refuse a value that is too long.</summary>
    private sealed class RefuseFirstGradeInterceptor : SaveChangesInterceptor
    {
        private int _refused;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var grade = eventData.Context!.ChangeTracker.Entries<AiGradingResult>()
                .Any(e => e.State == EntityState.Modified && e.Entity.Status == AiGradingResult.StatusGraded &&
                          e.Entity.Engine == FakeCodeRunner.EngineName);
            if (grade && Interlocked.Exchange(ref _refused, 1) == 0)
                throw new DbUpdateException("22001: value too long for type character varying(64)");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task M11_I_25_UnexpectedFailure_EndsAsFailed_NotPendingForEver()
    {
        var interceptor = new RefuseFirstGradeInterceptor();
        using var host = new TestHost(b => b.ConfigureTestServices(s =>
            s.ConfigureDbContext<Backend_API.Data.AppDbContext>(o => o.AddInterceptors(interceptor))));
        var student = await host.AsStudent();
        var sid = await host.StartSessionAsync(student, host.Seed.ExamId);
        (await TestHost.SaveAnswerAsync(student, sid, host.Seed.Question1Id, "int main(){}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TestHost.SubmitAsync(student, sid)).StatusCode.Should().Be(HttpStatusCode.OK);

        var grading = await GradingOfAsync(host.Seed.ExamId, sid, host.Seed.Question1Id, host);
        grading.GetProperty("status").GetString().Should().Be("Failed");
        grading.GetProperty("suggestedMarks").GetDouble().Should().Be(0);
        grading.GetProperty("justification").GetString().Should().Contain("internal error").And.Contain("teacher review required");
        grading.GetProperty("testCaseResults").EnumerateArray().Should().OnlyContain(r => r.GetProperty("status").GetString() == "NotRun");
        await TestHost.Eventually(() => Task.FromResult(host.Hub.Named("GradingCompleted").Any(m => m.ArgsJson.Contains("Failed"))),
            TimeSpan.FromSeconds(5), "the live monitor hears that grading ended");
    }
}
