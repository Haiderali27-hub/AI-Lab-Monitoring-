using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Auth;

public class AuthTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public AuthTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    [Fact]
    public async Task Login_Admin_WithValidCredentials_Returns200WithToken()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededData.AdminEmail,
            password = SeededData.AdminPassword
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        string? token = body.GetProperty("token").GetString();
        token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = SeededData.AdminEmail,
            password = "WrongPassword123!"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_WithNonExistentEmail_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "nobody@nowhere.com",
            password = "anything"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Student_WithCorrectHwid_Returns200AndDeviceBoundTrue()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/student-login", new
        {
            usernameOrEmail = SeededData.StudentEmail,
            password = SeededData.StudentPassword,
            hardwareFingerprint = SeededData.TestHwid
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
        envelope.GetProperty("success").GetBoolean().Should().BeTrue();
        
        var data = envelope.GetProperty("data");
        bool deviceBound = data.GetProperty("deviceBound").GetBoolean();
        deviceBound.Should().BeTrue();
    }

    [Fact]
    public async Task Login_Student_WithWrongHwid_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/student-login", new
        {
            usernameOrEmail = SeededData.StudentEmail,
            password = SeededData.StudentPassword,
            hardwareFingerprint = SeededData.WrongHwid
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_Student_WithoutHwid_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/student-login", new
        {
            usernameOrEmail = SeededData.StudentEmail,
            password = SeededData.StudentPassword
            // no hardwareFingerprint
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetMe_WithValidToken_Returns200WithUserInfo()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        string? email = body.GetProperty("email").GetString();
        email.Should().Be(SeededData.AdminEmail);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        _client.DefaultRequestHeaders.Authorization = null;
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_WithValidToken_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsync("/api/auth/logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
