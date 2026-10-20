using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Backend_API.Models;
using Backend_API.Models.Enums;
using Backend_API.Services.Grading;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Backend_API.Tests.Tests.Grading;

/// <summary>
/// M11 — the optional LLM features: off by default, robust JSON parsing, provider requests, safe fallbacks.
/// No network: a fake HTTP handler answers.
/// </summary>
public class LlmGradingTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Seen { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return respond(request);
        }
    }

    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static LlmClient Client(FakeHandler handler, LlmOptions options, string? key = "test-key") =>
        new(new FakeFactory(handler), options, NullLogger<LlmClient>.Instance, _ => key);

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string GroqReply(string content) =>
        new JsonObject { ["choices"] = new JsonArray { new JsonObject { ["message"] = new JsonObject { ["content"] = content } } } }.ToJsonString();

    private static readonly Question Theory = new()
    {
        Type = QuestionType.Theory, BodyText = "Explain stack vs queue.", Marks = 10, ReferenceAnswer = "Stack is LIFO, queue is FIFO."
    };

    [Fact]
    public void OffByDefault_AndWithoutAKey()
    {
        var handler = new FakeHandler(_ => Json("{}"));
        Client(handler, new LlmOptions()).IsEnabled.Should().BeFalse("Grading:Llm:Enabled defaults to false");
        Client(handler, new LlmOptions { Enabled = true }, key: null).IsEnabled.Should().BeFalse("no key in the environment");
        Client(handler, new LlmOptions { Enabled = true }).IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Disabled_NeverCallsTheNetwork()
    {
        var handler = new FakeHandler(_ => throw new InvalidOperationException("must not be called"));
        var grader = new LlmGrader(Client(handler, new LlmOptions()));
        (await grader.GradeTheoryAsync(Theory, "A stack is LIFO.", CancellationToken.None)).Should().BeNull();
        handler.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task Groq_TheoryGrade_IsClampedRounded_AndConfidenceCappedAtMedium()
    {
        var handler = new FakeHandler(_ => Json(GroqReply("```json\n{\"marks\": 12.3, \"justification\": \"Complete.\", \"confidence\": \"high\",}\n```")));
        var grade = await new LlmGrader(Client(handler, new LlmOptions { Enabled = true })).GradeTheoryAsync(Theory, "A stack is LIFO.", CancellationToken.None);

        grade.Should().NotBeNull();
        grade!.SuggestedMarks.Should().Be(10, "clamped to the question's marks");
        grade.Confidence.Should().Be("Medium");
        grade.Justification.Should().Contain("Complete.").And.Contain("teacher review required");
        grade.Engine.Should().StartWith("LLM (Groq");

        var (request, body) = handler.Seen.Single();
        request.RequestUri!.Host.Should().Be("api.groq.com");
        request.Headers.Authorization!.Parameter.Should().Be("test-key");
        body.Should().Contain("A stack is LIFO.").And.Contain("json_object");
    }

    [Fact]
    public async Task Gemini_SendsTheKeyInAHeader_NotTheUrl()
    {
        var reply = new JsonObject
        {
            ["candidates"] = new JsonArray { new JsonObject { ["content"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = "{\"marks\": 4.4}" } } } } }
        }.ToJsonString();
        var handler = new FakeHandler(_ => Json(reply));
        var grade = await new LlmGrader(Client(handler, new LlmOptions { Enabled = true, Provider = "Gemini" }))
            .GradeTheoryAsync(Theory, "x", CancellationToken.None);

        grade!.SuggestedMarks.Should().Be(4.5);
        var request = handler.Seen.Single().Request;
        request.RequestUri!.ToString().Should().NotContain("key=");
        request.Headers.GetValues("x-goog-api-key").Should().Equal("test-key");
    }

    [Fact]
    public async Task HttpError_OrGarbage_FallsBackToNull()
    {
        var failing = new FakeHandler(_ => Json("{\"error\":\"boom\"}", HttpStatusCode.InternalServerError));
        (await new LlmGrader(Client(failing, new LlmOptions { Enabled = true })).GradeTheoryAsync(Theory, "x", CancellationToken.None)).Should().BeNull();

        var garbage = new FakeHandler(_ => Json(GroqReply("I think it deserves full marks")));
        (await new LlmGrader(Client(garbage, new LlmOptions { Enabled = true })).GradeTheoryAsync(Theory, "x", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task RateLimited_IsRetried()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1
            ? Json("{\"error\":{\"message\":\"Rate limit reached. Please try again in 0.01s.\"}}", HttpStatusCode.TooManyRequests)
            : Json(GroqReply("{\"marks\": 7}")));
        var grade = await new LlmGrader(Client(handler, new LlmOptions { Enabled = true })).GradeTheoryAsync(Theory, "x", CancellationToken.None);
        grade!.SuggestedMarks.Should().Be(7);
        calls.Should().Be(2);
    }

    [Fact]
    public async Task CodeSummary_KeepsCompleteLines_AndSendsNoHiddenOutputs()
    {
        var handler = new FakeHandler(_ => Json(GroqReply("{\"summary\": \"Compiled cleanly.\\nPassed 1 of 2 cases.\\nCheck the hidden case\"}")));
        var cases = new[] { new ScoringCase(null, "c", "1", "1", 1, false), new ScoringCase(null, "h", "SECRET-IN", "SECRET-OUT", 1, true) };
        var score = TestCaseScorer.Score(10, cases, new RunReport(true, null, [new CaseRun(0, "1", 0, false, false, 1), new CaseRun(1, "x", 0, false, false, 1)], "t", 1));
        var analysis = CodeAnalyzer.Analyze("int main(){}", CodeLanguages.Cpp, score);

        var summary = await new LlmGrader(Client(handler, new LlmOptions { Enabled = true }))
            .SummariseCodeAsync(new Question { BodyText = "Add two numbers", Marks = 10 }, score, analysis, true, CancellationToken.None);

        summary.Should().Be("Compiled cleanly.\nPassed 1 of 2 cases.");
        handler.Seen.Single().Body.Should().NotContain("SECRET-IN").And.NotContain("SECRET-OUT");
    }

    [Theory]
    [InlineData("{\"a\": 1}", 1)]
    [InlineData("Sure! ```json\n{\"a\": 1,}\n``` hope that helps", 1)]
    [InlineData("{\"a\": 1, \"b\": [1, 2,]}", 1)]
    [InlineData("{\"a\": 1, \"b\": \"cut off", 1)]
    public void LlmJson_RecoversAnObject(string raw, int a) =>
        LlmJson.TryParseObject(raw)!["a"]!.GetValue<int>().Should().Be(a);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("[1, 2, 3]")]
    public void LlmJson_GivesUp(string? raw) => LlmJson.TryParseObject(raw).Should().BeNull();

    [Theory]
    [InlineData("Please try again in 2.5s.", 3.0)]
    [InlineData("try again in 0m30s", 30.5)]
    [InlineData("no hint", 12)]
    [InlineData("try again in 10m0s", 60)]
    public void RetryDelay_ParsesTheProviderHint(string body, double seconds) =>
        LlmClient.RetryDelay(body).TotalSeconds.Should().BeApproximately(seconds, 0.001);
}
