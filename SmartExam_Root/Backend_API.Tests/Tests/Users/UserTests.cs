using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Users;

public class UserTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public UserTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    [Fact]
    public async Task GetAllUsers_AsAdmin_Returns200WithUserList()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/users");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        body.Should().HaveCountGreaterThanOrEqualTo(4); // admin + teacher + 2 students
    }

    [Fact]
    public async Task GetAllUsers_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/users");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_FilterByStudentRole_ReturnsOnlyStudents()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/users?role=Student");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        body.Should().HaveCountGreaterThanOrEqualTo(2);
        foreach (var user in body!)
            user.GetProperty("role").GetString().Should().Be("Student");
    }

    [Fact]
    public async Task CreateUser_AsAdmin_Returns201WithNewUser()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.PostAsJsonAsync("/api/users", new
        {
            name = "New Test Student",
            email = $"newstudent_{Guid.NewGuid()}@test.com",
            password = "NewPass@123",
            role = "Student"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("role").GetString().Should().Be("Student");
    }

    [Fact]
    public async Task CreateUser_WithDuplicateEmail_Returns409()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var uniqueEmail = $"duplicate_{Guid.NewGuid()}@test.com";
        await _client.PostAsJsonAsync("/api/users", new
        {
            name = "Duplicate", email = uniqueEmail, password = "Pass@123", role = "Student"
        });
        // Second call with same email
        var response = await _client.PostAsJsonAsync("/api/users", new
        {
            name = "Duplicate Again", email = uniqueEmail, password = "Pass@123", role = "Student"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateUser_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PostAsJsonAsync("/api/users", new
        {
            name = "Hacker", email = "hacker@test.com", password = "Hack@123", role = "Admin"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ResetDeviceBinding_AsAdmin_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.DeleteAsync($"/api/users/{_seed.StudentId}/device-binding");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ForceLogout_AsAdmin_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.PostAsync($"/api/users/{_seed.TeacherId}/force-logout", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
