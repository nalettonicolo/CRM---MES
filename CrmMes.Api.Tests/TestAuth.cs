using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

internal static class TestAuth
{
    public static async Task<AuthResponse> RegisterAsync(HttpClient client, string name, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(name, email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static Task<HttpResponseMessage> LoginRawAsync(HttpClient client, string email, string password)
        => client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));

    public static async Task<AuthResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await LoginRawAsync(client, email, password);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    /// <summary>Returns a new HttpClient bound to the same test server, authenticated as the given token.</summary>
    public static HttpClient AuthenticatedClient(this CustomWebApplicationFactory factory, string token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Registers a new user with the given role using an admin-authenticated client, then logs them in.</summary>
    public static async Task<AuthResponse> CreateUserWithRoleAsync(
        CustomWebApplicationFactory factory,
        string adminToken,
        string role,
        string? emailPrefix = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"{emailPrefix ?? role.ToLowerInvariant()}-{suffix}@test.local";
        const string password = "TestPass123!";

        using var adminClient = factory.AuthenticatedClient(adminToken);
        var response = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest($"{role} Tester", email, password, role));
        response.EnsureSuccessStatusCode();

        using var anonymousClient = factory.CreateClient();
        return await LoginAsync(anonymousClient, email, password);
    }
}
