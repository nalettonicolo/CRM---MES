using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class AuthRefreshTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public AuthRefreshTests(AdminSeededApiTestFixture fixture) => _fixture = fixture;

    // La registrazione pubblica è bootstrap-only (vedi AuthBootstrapTests), quindi ogni utente oltre il
    // primo va creato tramite un Admin autenticato, non con TestAuth.RegisterAsync.
    private Task<AuthResponse> RegisterUniqueUserAsync(string prefix) =>
        TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", prefix);

    [Fact]
    public async Task Login_ReturnsUsableRefreshTokenAndExpiry()
    {
        var auth = await RegisterUniqueUserAsync("login-refresh");

        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.True(auth.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task Refresh_WithValidToken_IssuesNewAccessToken()
    {
        var auth = await RegisterUniqueUserAsync("refresh-ok");

        var response = await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var refreshed = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.NotEqual(auth.Token, refreshed.Token);
        Assert.NotEqual(auth.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(auth.UserId, refreshed.UserId);
    }

    [Fact]
    public async Task Refresh_RotatesToken_OldRefreshTokenNoLongerWorks()
    {
        var auth = await RegisterUniqueUserAsync("refresh-rotate");

        await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));

        var secondAttempt = await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithUnknownToken_ReturnsUnauthorized()
    {
        var response = await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("not-a-real-token"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesToken_SubsequentRefreshFails()
    {
        var auth = await RegisterUniqueUserAsync("logout");

        var logoutResponse = await _fixture.Client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);

        var refreshResponse = await _fixture.Client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }
}
