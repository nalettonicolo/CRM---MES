using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class AuthExternalTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public AuthExternalTests(AdminSeededApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ExternalLogin_ProvidersAutoProvisionAndRefusal()
    {
        var providers = await _fixture.Client.GetFromJsonAsync<List<ExternalProviderResponse>>("/api/auth/external/providers");
        Assert.Contains(providers!, p => p.Key == "dev" && p.Enabled);

        await using var noProvisionFactory = new CustomWebApplicationFactory { ExternalAutoProvision = false };
        await noProvisionFactory.InitializeDatabaseAsync();
        using var noProvisionClient = noProvisionFactory.CreateClient();
        var refused = await noProvisionClient.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "dev", "unknown-subject", "nuovo@test.local", "Nuovo", "web"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var email = $"sso-{Guid.NewGuid():N}@test.local";
        var response = await _fixture.Client.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "dev", $"sub-{Guid.NewGuid():N}", email, "SSO Tester", "web"));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
        Assert.Equal("Operator", auth.Role);
        Assert.Equal(email, auth.Email);

        var again = await _fixture.Client.PostAsJsonAsync("/api/auth/external", new ExternalLoginRequest(
            "dev", $"other-{Guid.NewGuid():N}", email, "SSO Tester", "web"));
        again.EnsureSuccessStatusCode();
        var same = (await again.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.Equal(auth.UserId, same.UserId);
    }
}
