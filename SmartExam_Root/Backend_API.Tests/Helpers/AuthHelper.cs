using System.Net.Http.Json;
using System.Text.Json;

namespace Backend_API.Tests.Helpers;

public static class AuthHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public static async Task<string> GetTokenAsync(HttpClient client, string email, string password, string? hwid = null)
    {
        var payload = hwid != null
            ? new { email, password, hwidHash = hwid }
            : (object)new { email, password };

        var response = await client.PostAsJsonAsync("/api/auth/login", payload);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("token").GetString()!;
    }

    public static void SetToken(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public static async Task AuthorizeAs(HttpClient client, string email, string password, string? hwid = null)
    {
        var token = await GetTokenAsync(client, email, password, hwid);
        SetToken(client, token);
    }
}
