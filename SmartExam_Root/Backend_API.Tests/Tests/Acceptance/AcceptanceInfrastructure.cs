using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Backend_API.Data;
using Backend_API.Hubs;
using Backend_API.Services;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend_API.Tests.Tests.Acceptance;

/// <summary>
/// One isolated backend (own InMemory DB + seed) for an acceptance test, with helpers that drive it
/// purely over HTTP the way the web panel / WPF client do. The SignalR hub context is replaced by a
/// <see cref="HubRecorder"/> so tests can assert on broadcasts without a SignalR client.
/// </summary>
public sealed class TestHost : IDisposable
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private readonly TestWebAppFactory _factory;
    public WebApplicationFactory<Program> App { get; }
    public HubRecorder Hub { get; } = new();
    public SeededData Seed { get; }
    private readonly List<HttpClient> _clients = new();

    public TestHost(Action<IWebHostBuilder>? configure = null)
    {
        _factory = new TestWebAppFactory();
        App = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IHubContext<MonitoringHub>>(Hub);
                s.AddTransient<IStartupFilter, TestRemoteIpStartupFilter>();
            });
            configure?.Invoke(b);
        });
        // Building the server runs TestWebAppFactory.ConfigureWebHost, which seeds and sets SeededData.
        _ = App.Server;
        Seed = _factory.SeededData;
    }

    public IServiceProvider Services => App.Services;

    public HttpClient NewClient()
    {
        var c = App.CreateClient();
        _clients.Add(c);
        return c;
    }

    public void Dispose()
    {
        foreach (var c in _clients) c.Dispose();
        App.Dispose();
        _factory.Dispose();
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public async Task<HttpClient> LoginAsync(string email, string password)
    {
        var c = NewClient();
        await AuthHelper.AuthorizeAs(c, email, password);
        return c;
    }

    public Task<HttpClient> AsAdmin() => LoginAsync(SeededData.AdminEmail, SeededData.AdminPassword);
    public Task<HttpClient> AsTeacher() => LoginAsync(SeededData.TeacherEmail, SeededData.TeacherPassword);
    public Task<HttpClient> AsStudent() => LoginAsync(SeededData.StudentEmail, SeededData.StudentPassword);
    public Task<HttpClient> AsStudent2() => LoginAsync(SeededData.Student2Email, SeededData.Student2Password);

    /// <summary>Raw student-client login (the WPF path). Returns the raw response.</summary>
    public async Task<HttpResponseMessage> StudentLoginRawAsync(HttpClient c, string identifier, string password, string? hwid,
        string? machineName = null, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["usernameOrEmail"] = identifier,
            ["password"] = password,
            ["hardwareFingerprint"] = hwid,
            ["machineName"] = machineName,
            ["windowsUsername"] = "labuser"
        };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return await c.PostAsJsonAsync("/api/auth/student-login", body);
    }

    /// <summary>Student-client login that authorizes the returned client with the issued access token.</summary>
    public async Task<HttpClient> StudentClientLoginAsync(string email, string password, string hwid, string? machineName = null)
    {
        var c = NewClient();
        var resp = await StudentLoginRawAsync(c, email, password, hwid, machineName);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        AuthHelper.SetToken(c, body.GetProperty("data").GetProperty("accessToken").GetString()!);
        return c;
    }

    // ── Users / exams ─────────────────────────────────────────────────────────

    public async Task<(Guid Id, string Email, string Password)> CreateUserAsync(string role, string? name = null)
    {
        var admin = await AsAdmin();
        var email = $"{role.ToLowerInvariant()}_{Guid.NewGuid():N}@test.com";
        const string password = "Pass@1234";
        var resp = await admin.PostAsJsonAsync("/api/users", new { name = name ?? $"{role} {email[..12]}", email, password, role });
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        var id = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("userId").GetGuid();
        return (id, email, password);
    }

    public static object TheoryQuestion(string body = "Explain stack vs queue.", int marks = 10) => new
    {
        type = "Theory", bodyText = body, marks, testCases = Array.Empty<object>(),
        options = (string[]?)null, correctOptionIndex = (int?)null, referenceAnswer = "Stack is LIFO, queue is FIFO."
    };

    public static object CodingQuestion(int marks = 20, params (string input, string output, bool hidden)[] cases) => new
    {
        type = "Coding", bodyText = "Print the sum of two integers.", marks,
        testCases = (cases.Length == 0 ? new[] { ("1 2", "3", false) } : cases)
            .Select(tc => new { input = tc.Item1, expectedOutput = tc.Item2, isHidden = tc.Item3 }).ToArray(),
        options = (string[]?)null, correctOptionIndex = (int?)null, referenceAnswer = (string?)null
    };

    /// <summary>
    /// Creates an exam through the teacher API. <paramref name="extra"/> carries additional (possibly
    /// planned, not-yet-built) fields such as requireReadiness / isMock / lockdownMode.
    /// </summary>
    public async Task<Guid> CreateExamAsync(IEnumerable<Guid> studentIds, DateTime? startUtc = null,
        IDictionary<string, object?>? extra = null, object[]? questions = null, int durationMinutes = 60, HttpClient? teacher = null)
    {
        teacher ??= await AsTeacher();
        var payload = new Dictionary<string, object?>
        {
            ["title"] = $"Acceptance Exam {Guid.NewGuid():N}"[..28],
            ["instructions"] = "Acceptance test exam.",
            ["sectionId"] = Seed.SectionId,
            ["startTime"] = startUtc ?? DateTime.UtcNow.AddMinutes(-1),
            ["durationMinutes"] = durationMinutes,
            ["allowedApps"] = "[\"code.exe\"]",
            ["aiEvaluationEnabled"] = true,
            ["plagiarismThreshold"] = 70,
            ["questions"] = questions ?? new[] { TheoryQuestion() },
            ["studentIds"] = studentIds.ToArray()
        };
        if (extra is not null) foreach (var kv in extra) payload[kv.Key] = kv.Value;

        var resp = await teacher.PostAsJsonAsync("/api/exams", payload);
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("examId").GetGuid();
    }

    public async Task<HttpResponseMessage> StartSessionRawAsync(HttpClient student, Guid examId) =>
        await student.PostAsync($"/api/exams/{examId}/start-session", null);

    public async Task<Guid> StartSessionAsync(HttpClient student, Guid examId)
    {
        var resp = await StartSessionRawAsync(student, examId);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sessionId").GetGuid();
    }

    public static Task<HttpResponseMessage> SaveAnswerAsync(HttpClient student, Guid sessionId, Guid questionId, string text, object? extra = null)
    {
        var body = new Dictionary<string, object?> { ["questionId"] = questionId, ["answerText"] = text };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        return student.PostAsJsonAsync($"/api/exams/sessions/{sessionId}/save-answer", body);
    }

    public static Task<HttpResponseMessage> SubmitAsync(HttpClient student, Guid sessionId) =>
        student.PostAsync($"/api/exams/sessions/{sessionId}/submit", null);

    public static Task<HttpResponseMessage> PostEventAsync(HttpClient student, Guid sessionId, string eventType, string payloadJson = "{}") =>
        student.PostAsJsonAsync("/api/monitoring/event", new { eventType, examSessionId = sessionId, payloadJson });

    public static Task<HttpResponseMessage> HeartbeatAsync(HttpClient student, Guid sessionId) =>
        student.PostAsJsonAsync("/api/monitoring/heartbeat", new
        {
            examSessionId = sessionId, isForegroundExamApp = true, activeWindowTitle = "SmartExam", processListSnapshot = "SmartExam.exe"
        });

    public async Task<List<Guid>> QuestionIdsAsync(Guid examId) =>
        await WithDbAsync(db => db.Questions.Where(q => q.ExamId == examId).OrderBy(q => q.OrderIndex).Select(q => q.QuestionId).ToListAsync());

    public async Task<List<JsonElement>> AuditLogsAsync(string? eventType = null)
    {
        var admin = await AsAdmin();
        var url = eventType is null ? "/api/audit-logs?take=1000" : $"/api/audit-logs?eventType={eventType}&take=1000";
        var resp = await admin.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resp.Content.ReadFromJsonAsync<List<JsonElement>>())!;
    }

    // ── Direct DB access (only for setup that has no API, and for verifying persisted state) ──

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> f)
    {
        using var scope = Services.CreateScope();
        return await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> f)
    {
        using var scope = Services.CreateScope();
        await f(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Moves the exam's window so that it ended <paramref name="minutesAgo"/> minutes ago.</summary>
    public Task EndExamWindowAsync(Guid examId, int minutesAgo) => WithDbAsync(async db =>
    {
        var exam = await db.Exams.SingleAsync(e => e.ExamId == examId);
        exam.StartTime = DateTime.UtcNow.AddMinutes(-(exam.DurationMinutes + minutesAgo));
        await db.SaveChangesAsync();
    });

    // ── Direct hub invocation (no SignalR client package needed) ───────────────

    public static ClaimsPrincipal Principal(Guid userId, string role) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role)
        }, "Test", ClaimTypes.Name, ClaimTypes.Role));

    /// <summary>Creates a MonitoringHub instance wired to a fake caller context for <paramref name="user"/>.</summary>
    public HubSession CreateHub(ClaimsPrincipal user)
    {
        var scope = Services.CreateScope();
        var hub = ActivatorUtilities.CreateInstance<MonitoringHub>(scope.ServiceProvider);
        var groups = new RecordingGroupManager();
        hub.Context = new FakeHubCallerContext(user);
        hub.Groups = groups;
        hub.Clients = new RecordingCallerClients(Hub);
        return new HubSession(hub, scope, groups);
    }

    // ── Response helpers ──────────────────────────────────────────────────────

    public static async Task<string?> ErrorCodeAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var el = JsonDocument.Parse(text).RootElement;
            return el.ValueKind == JsonValueKind.Object && el.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException) { return null; }
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage resp) =>
        await resp.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Returns <c>data</c> when the body is an ApiEnvelope, else the body itself.</summary>
    public static async Task<JsonElement> DataAsync(HttpResponseMessage resp)
    {
        var el = await JsonAsync(resp);
        return el.ValueKind == JsonValueKind.Object && el.TryGetProperty("data", out var d) && el.TryGetProperty("code", out _) ? d : el;
    }

    public static async Task Eventually(Func<Task<bool>> condition, TimeSpan timeout, string because)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return;
            await Task.Delay(250);
        }
        (await condition()).Should().BeTrue(because);
    }
}

/// <summary>Reflection helpers for tables/columns that only exist in the PLANNED schema, so pending tests compile today.</summary>
public static class Planned
{
    public static IEnumerable<object> Set(AppDbContext db, string dbSetName)
    {
        var prop = db.GetType().GetProperty(dbSetName)
                   ?? throw new InvalidOperationException($"AppDbContext has no DbSet '{dbSetName}' yet (planned feature).");
        return ((IEnumerable<object>)prop.GetValue(db)!).ToList();
    }

    public static object? Get(object entity, string property) =>
        (entity.GetType().GetProperty(property)
         ?? throw new InvalidOperationException($"{entity.GetType().Name}.{property} does not exist yet (planned feature)."))
        .GetValue(entity);

    public static void Set(object entity, string property, object? value) =>
        (entity.GetType().GetProperty(property)
         ?? throw new InvalidOperationException($"{entity.GetType().Name}.{property} does not exist yet (planned feature)."))
        .SetValue(entity, value);

    /// <summary>Invokes a hub method by name (planned hub methods don't exist yet), adapting Guid/string args.</summary>
    public static async Task InvokeHubAsync(Hub hub, string method, params object[] args)
    {
        var mi = hub.GetType().GetMethods().FirstOrDefault(m => m.Name == method && m.GetParameters().Length == args.Length)
                 ?? throw new MissingMethodException(hub.GetType().Name, method);
        var ps = mi.GetParameters();
        var converted = new object?[args.Length];
        for (var i = 0; i < args.Length; i++)
        {
            var t = ps[i].ParameterType;
            converted[i] = args[i] switch
            {
                Guid g when t == typeof(string) => g.ToString(),
                string s when t == typeof(Guid) => Guid.Parse(s),
                var a => a
            };
        }
        try
        {
            if (mi.Invoke(hub, converted) is Task task) await task;
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            throw tie.InnerException;
        }
    }
}

// ── SignalR fakes ────────────────────────────────────────────────────────────────

public record HubMessage(string Target, string Method, string ArgsJson);

public sealed class HubRecorder : IHubContext<MonitoringHub>
{
    public ConcurrentQueue<HubMessage> Messages { get; } = new();
    public IHubClients Clients { get; }
    public RecordingGroupManager Groups { get; } = new();
    IGroupManager IHubContext<MonitoringHub>.Groups => Groups;

    public HubRecorder() => Clients = new RecordingHubClients(this);

    public IEnumerable<HubMessage> Named(string method) => Messages.Where(m => m.Method == method);

    internal IClientProxy Proxy(string target) => new RecordingProxy(this, target);
}

internal sealed class RecordingHubClients(HubRecorder r) : IHubClients
{
    public IClientProxy All => r.Proxy("all");
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => r.Proxy("all");
    public IClientProxy Client(string connectionId) => r.Proxy($"conn:{connectionId}");
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => r.Proxy("conns");
    public IClientProxy Group(string groupName) => r.Proxy(groupName);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => r.Proxy(groupName);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => r.Proxy(string.Join(",", groupNames));
    public IClientProxy User(string userId) => r.Proxy($"user:{userId}");
    public IClientProxy Users(IReadOnlyList<string> userIds) => r.Proxy($"users:{string.Join(",", userIds)}");
}

internal sealed class RecordingProxy(HubRecorder recorder, string target) : ISingleClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        recorder.Messages.Enqueue(new HubMessage(target, method, JsonSerializer.Serialize(args, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        return Task.CompletedTask;
    }

    public Task<T> InvokeCoreAsync<T>(string method, object?[] args, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}

internal sealed class RecordingCallerClients(HubRecorder recorder) : IHubCallerClients
{
    public ISingleClientProxy Caller => new RecordingProxy(recorder, "caller");
    IClientProxy IHubCallerClients<IClientProxy>.Caller => Caller;
    public IClientProxy Others => recorder.Proxy("others");
    public IClientProxy OthersInGroup(string groupName) => recorder.Proxy(groupName);
    public IClientProxy All => recorder.Proxy("all");
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => recorder.Proxy("all");
    public ISingleClientProxy Client(string connectionId) => new RecordingProxy(recorder, $"conn:{connectionId}");
    IClientProxy IHubClients<IClientProxy>.Client(string connectionId) => Client(connectionId);
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => recorder.Proxy("conns");
    public IClientProxy Group(string groupName) => recorder.Proxy(groupName);
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => recorder.Proxy(groupName);
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => recorder.Proxy(string.Join(",", groupNames));
    public IClientProxy User(string userId) => recorder.Proxy($"user:{userId}");
    public IClientProxy Users(IReadOnlyList<string> userIds) => recorder.Proxy($"users:{string.Join(",", userIds)}");
}

public sealed class RecordingGroupManager : IGroupManager
{
    public ConcurrentBag<(string ConnectionId, string Group)> Added { get; } = new();

    public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Added.Add((connectionId, groupName));
        return Task.CompletedTask;
    }

    public ConcurrentBag<(string ConnectionId, string Group)> Removed { get; } = new();

    public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        Removed.Add((connectionId, groupName));
        return Task.CompletedTask;
    }
}

internal sealed class FakeHubCallerContext(ClaimsPrincipal user) : HubCallerContext
{
    public override string ConnectionId { get; } = Guid.NewGuid().ToString("N");
    public override string? UserIdentifier => user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    public override ClaimsPrincipal? User => user;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() { }
}

public sealed class HubSession(MonitoringHub hub, IServiceScope scope, RecordingGroupManager groups) : IDisposable
{
    public MonitoringHub Hub { get; } = hub;
    public RecordingGroupManager Groups { get; } = groups;
    public AppDbContext Db => scope.ServiceProvider.GetRequiredService<AppDbContext>();
    public void Dispose()
    {
        Hub.Dispose();
        scope.Dispose();
    }
}

/// <summary>
/// Lets a test choose the client IP (TestServer has none) via the X-Test-Remote-IP header —
/// needed for per-IP rate limiting and IP-based seat matching.
/// </summary>
internal sealed class TestRemoteIpStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (ctx, nextMw) =>
        {
            if (ctx.Request.Headers.TryGetValue("X-Test-Remote-IP", out var ip) && IPAddress.TryParse(ip, out var addr))
                ctx.Connection.RemoteIpAddress = addr;
            await nextMw();
        });
        next(app);
    };
}

public static class HttpClientExtensions
{
    public static HttpClient FromIp(this HttpClient c, string ip)
    {
        c.DefaultRequestHeaders.Remove("X-Test-Remote-IP");
        c.DefaultRequestHeaders.Add("X-Test-Remote-IP", ip);
        return c;
    }
}
