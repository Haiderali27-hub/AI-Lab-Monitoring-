using System.Net;
using System.Net.Http.Json;
using Backend_API.Tests.Helpers;
using FluentAssertions;
using System.Text.Json;

namespace Backend_API.Tests.Tests.Notifications;

public class NotificationTests : IClassFixture<TestWebAppFactory>
{
    private readonly HttpClient _client;
    private readonly SeededData _seed;

    public NotificationTests(TestWebAppFactory factory)
    {
        _client = factory.CreateClient();
        _seed = factory.SeededData;
    }

    [Fact]
    public async Task GetNotifications_AsAdmin_Returns200WithUnreadCount()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.AdminEmail, SeededData.AdminPassword);
        var response = await _client.GetAsync("/api/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("unreadCount").Should().NotBeNull();
        body.GetProperty("notifications").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task NotifyExamScheduled_AsTeacher_Returns200AndStudentsReceiveNotification()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var notifyResp = await _client.PostAsync(
            $"/api/notifications/exam/{_seed.ExamId}/notify-scheduled", null);

        notifyResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Now check student received it
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var notifResp = await _client.GetAsync("/api/notifications");
        var body = await notifResp.Content.ReadFromJsonAsync<JsonElement>();
        int unread = body.GetProperty("unreadCount").GetInt32();
        unread.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MarkAllNotificationsRead_Returns200WithMarkedCount()
    {
        // First send a notification so there's something to mark
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        await _client.PostAsync($"/api/notifications/exam/{_seed.ExamId}/notify-scheduled", null);

        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PatchAsync("/api/notifications/read-all", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("markedRead").GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task SendAnnouncement_AsTeacher_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsJsonAsync(
            $"/api/notifications/exam/{_seed.ExamId}/announce",
            new { title = "Test Announcement", message = "This is a test message.", sendEmail = false }
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Contain("students");
    }

    [Fact]
    public async Task SendAnnouncement_AsStudent_Returns403()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.PostAsJsonAsync(
            $"/api/notifications/exam/{_seed.ExamId}/announce",
            new { title = "Hack", message = "hacked", sendEmail = false }
        );
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetStudentNotifications_AsStudent_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.StudentEmail, SeededData.StudentPassword, SeededData.TestHwid);
        var response = await _client.GetAsync("/api/student/notifications");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("unreadCount").Should().NotBeNull();
    }

    [Fact]
    public async Task NotifyGradesReleased_AsTeacher_Returns200()
    {
        await AuthHelper.AuthorizeAs(_client, SeededData.TeacherEmail, SeededData.TeacherPassword);
        var response = await _client.PostAsync(
            $"/api/notifications/exam/{_seed.ExamId}/notify-grades", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
