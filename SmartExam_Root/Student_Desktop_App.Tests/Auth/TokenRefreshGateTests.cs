using FluentAssertions;
using Student_Desktop_App.Core.Auth;
using Student_Desktop_App.Services;

namespace Student_Desktop_App.Tests.Auth;

/// <summary>
/// Module 1 desktop refresh flow. The server treats a replayed refresh token as theft and revokes the
/// whole family, so the client must refresh single-flight and keep its session on transient errors.
/// </summary>
public class TokenRefreshGateTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private static TokenResponse Token(string access, string refresh, DateTime expires) =>
        new(access, refresh, expires, new UserSummary(Guid.Empty, Guid.Empty, "s", "s@x", "Student"), true);

    private sealed class Harness
    {
        public TokenResponse? Current;
        public int RefreshCalls;
        public List<string> RefreshTokensSent { get; } = new();
        public Func<string, Task<RefreshAttempt>> OnRefresh = _ => Task.FromResult(RefreshAttempt.Rejected);
        public DateTime Clock = Now;

        public TokenRefreshGate Gate() => new(() => Current, t => Current = t, async (rt, _) =>
        {
            Interlocked.Increment(ref RefreshCalls);
            lock (RefreshTokensSent) RefreshTokensSent.Add(rt);
            return await OnRefresh(rt);
        }, () => Clock);
    }

    [Fact]
    public async Task ConcurrentUnauthorized_RefreshOnlyOnce_AndBothGetTheNewToken()
    {
        var h = new Harness { Current = Token("a1", "r1", Now.AddMinutes(20)) };
        var release = new TaskCompletionSource<RefreshAttempt>();
        h.OnRefresh = _ => release.Task;
        var gate = h.Gate();

        // Heartbeat and answer-save both got 401 with a1.
        var first = gate.RefreshAsync("a1");
        var second = gate.RefreshAsync("a1");
        release.SetResult(new RefreshAttempt(RefreshStatus.Refreshed, Token("a2", "r2", Now.AddMinutes(30))));

        var results = await Task.WhenAll(first, second);
        h.RefreshCalls.Should().Be(1, "replaying r1 would trip reuse detection and log the student out");
        results.Should().OnlyContain(r => r.Status == RefreshStatus.Refreshed && r.Token!.AccessToken == "a2");
        h.Current!.RefreshToken.Should().Be("r2");
    }

    [Fact]
    public async Task StaleFailure_AfterSomeoneElseRefreshed_DoesNotRefreshAgain()
    {
        var h = new Harness { Current = Token("a2", "r2", Now.AddMinutes(30)) };
        var attempt = await h.Gate().RefreshAsync("a1"); // a request that was sent with the old token
        attempt.Token!.AccessToken.Should().Be("a2");
        h.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAccessToken_FreshToken_IsReturnedWithoutRefreshing()
    {
        var h = new Harness { Current = Token("a1", "r1", Now.AddMinutes(10)) };
        (await h.Gate().GetAccessTokenAsync()).Should().Be("a1");
        h.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAccessToken_NearExpiry_RefreshesProactively()
    {
        var h = new Harness { Current = Token("a1", "r1", Now.AddSeconds(30)) };
        h.OnRefresh = _ => Task.FromResult(new RefreshAttempt(RefreshStatus.Refreshed, Token("a2", "r2", Now.AddMinutes(30))));

        (await h.Gate().GetAccessTokenAsync()).Should().Be("a2");
        h.RefreshTokensSent.Should().Equal("r1");
        h.Current!.AccessToken.Should().Be("a2");
    }

    [Fact]
    public async Task Unavailable_KeepsTheCurrentSession()
    {
        var h = new Harness { Current = Token("a1", "r1", Now.AddSeconds(30)) };
        h.OnRefresh = _ => Task.FromResult(RefreshAttempt.Unavailable);
        var gate = h.Gate();

        (await gate.GetAccessTokenAsync()).Should().Be("a1");
        (await gate.RefreshAsync("a1")).Status.Should().Be(RefreshStatus.Unavailable);
        h.Current!.AccessToken.Should().Be("a1");
        h.Current.RefreshToken.Should().Be("r1", "the unused refresh token stays valid for the next try");
    }

    [Fact]
    public async Task Rejected_IsReported_AndNothingIsStored()
    {
        var h = new Harness { Current = Token("a1", "r1", Now.AddMinutes(10)) };
        h.OnRefresh = _ => Task.FromResult(RefreshAttempt.Rejected);

        (await h.Gate().RefreshAsync("a1")).Status.Should().Be(RefreshStatus.Rejected);
        h.Current!.AccessToken.Should().Be("a1");
    }

    [Fact]
    public async Task LoggedOut_NeverRefreshes()
    {
        var h = new Harness();
        var gate = h.Gate();
        (await gate.GetAccessTokenAsync()).Should().BeNull();
        (await gate.RefreshAsync("a1")).Status.Should().Be(RefreshStatus.Rejected);
        h.RefreshCalls.Should().Be(0);
    }

    [Fact]
    public async Task LogoutDuringRefresh_DoesNotResurrectTheSession()
    {
        var h = new Harness { Current = Token("a1", "r1", Now.AddMinutes(10)) };
        h.OnRefresh = _ =>
        {
            h.Current = null; // user pressed Logout while the refresh was in flight
            return Task.FromResult(new RefreshAttempt(RefreshStatus.Refreshed, Token("a2", "r2", Now.AddMinutes(30))));
        };

        (await h.Gate().RefreshAsync("a1")).Status.Should().Be(RefreshStatus.Rejected);
        h.Current.Should().BeNull();
    }

    [Fact]
    public async Task MissingRefreshToken_IsRejected()
    {
        var h = new Harness { Current = Token("a1", "", Now.AddMinutes(10)) };
        (await h.Gate().RefreshAsync("a1")).Status.Should().Be(RefreshStatus.Rejected);
        h.RefreshCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("NETWORK_ERROR", true)]
    [InlineData("HTTP_429", true)]
    [InlineData("HTTP_503", true)]
    [InlineData("HTTP_500", true)]
    [InlineData("REFRESH_TOKEN_REUSED", false)]
    [InlineData("INVALID_REFRESH_TOKEN", false)]
    [InlineData("HTTP_401", false)]
    public void TransientFailures_DoNotEndTheSession(string code, bool transient) =>
        ApiClient.IsTransientFailure(code).Should().Be(transient);
}
