using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

/// <summary>Guards added after the 2026-09-29 security review: account lockout, request limits on the
/// endpoints that accept guesses, unique terminal PINs, catalog imports restricted by role.</summary>
public class SecurityTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public SecurityTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<string> CreateUserAsync(string role, string password = "TestPass123!")
    {
        var email = $"sec-{Guid.NewGuid():N}"[..16] + "@test.local";
        (await _adminClient.PostAsJsonAsync("/api/users", new CreateUserRequest("Sec Tester", email, password, role))).EnsureSuccessStatusCode();
        return email;
    }

    [Fact]
    public async Task Login_LocksAccountAfterFiveWrongPasswords_EvenForTheRightOne()
    {
        var email = await CreateUserAsync("Operator");
        using var client = _fixture.Factory.CreateClient();

        for (var attempt = 0; attempt < AuthController.MaxFailedLogins; attempt++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await TestAuth.LoginRawAsync(client, email, "wrong-password")).StatusCode);
        }

        var locked = await TestAuth.LoginRawAsync(client, email, "TestPass123!");
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Contains("bloccato", await locked.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Login_SuccessResetsTheFailureCount()
    {
        var email = await CreateUserAsync("Operator");
        using var client = _fixture.Factory.CreateClient();

        for (var attempt = 0; attempt < AuthController.MaxFailedLogins - 1; attempt++)
        {
            await TestAuth.LoginRawAsync(client, email, "wrong-password");
        }

        Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginRawAsync(client, email, "TestPass123!")).StatusCode);
        for (var attempt = 0; attempt < AuthController.MaxFailedLogins - 1; attempt++)
        {
            await TestAuth.LoginRawAsync(client, email, "wrong-password");
        }

        Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginRawAsync(client, email, "TestPass123!")).StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_GetsTheSameAnswerAsAWrongPassword()
    {
        using var client = _fixture.Factory.CreateClient();
        var unknown = await TestAuth.LoginRawAsync(client, "nobody-here@test.local", "whatever-123");
        var email = await CreateUserAsync("Operator");
        var wrong = await TestAuth.LoginRawAsync(client, email, "whatever-123");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrent_AndRevokesOtherSessions()
    {
        var email = await CreateUserAsync("Operator");
        using var anonymous = _fixture.Factory.CreateClient();
        var session = await TestAuth.LoginAsync(anonymous, email, "TestPass123!");
        using var client = _fixture.Factory.AuthenticatedClient(session.Token);

        var wrongCurrent = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("not-it", "NuovaPass456!"));
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        var tooShort = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("TestPass123!", "corta"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var changed = await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("TestPass123!", "NuovaPass456!"));
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await TestAuth.LoginRawAsync(anonymous, email, "TestPass123!")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginRawAsync(anonymous, email, "NuovaPass456!")).StatusCode);
        // The refresh token issued before the change no longer works.
        var refresh = await anonymous.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task AdminResetPassword_UnlocksAccount_AndIsAdminOnly()
    {
        var email = await CreateUserAsync("Operator");
        var users = (await _adminClient.GetFromJsonAsync<List<UserResponse>>("/api/users"))!;
        var userId = users.Single(u => u.Email == email).Id;
        using var anonymous = _fixture.Factory.CreateClient();
        for (var attempt = 0; attempt < AuthController.MaxFailedLogins; attempt++)
        {
            await TestAuth.LoginRawAsync(anonymous, email, "wrong-password");
        }

        var operatorSession = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "reset-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorSession.Token);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await operatorClient.PutAsJsonAsync($"/api/users/{userId}/password", new ResetUserPasswordRequest("Presa1234!"))).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await _adminClient.PutAsJsonAsync($"/api/users/{userId}/password", new ResetUserPasswordRequest("Reimpostata1!"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestAuth.LoginRawAsync(anonymous, email, "Reimpostata1!")).StatusCode);
    }

    [Fact]
    public async Task SetPin_AlreadyUsedByAnotherUser_IsRejected()
    {
        var first = await CreateUserAsync("Operator");
        var second = await CreateUserAsync("Operator");
        var users = (await _adminClient.GetFromJsonAsync<List<UserResponse>>("/api/users"))!;
        var firstId = users.Single(u => u.Email == first).Id;
        var secondId = users.Single(u => u.Email == second).Id;
        var pin = Random.Shared.Next(100000, 999999).ToString();

        Assert.Equal(HttpStatusCode.NoContent, (await _adminClient.PutAsJsonAsync($"/api/users/{firstId}/pin", new SetUserPinRequest(pin))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _adminClient.PutAsJsonAsync($"/api/users/{secondId}/pin", new SetUserPinRequest(pin))).StatusCode);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Sales")]
    public async Task CatalogImport_IsForbiddenOutsidePurchasingAndWarehouse(string role)
    {
        var email = await CreateUserAsync(role);
        using var anonymous = _fixture.Factory.CreateClient();
        var auth = await TestAuth.LoginAsync(anonymous, email, "TestPass123!");
        using var client = _fixture.Factory.AuthenticatedClient(auth.Token);

        using var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes("supplierCode,partNumber,materialCode,unitPrice\nX,Y,Z,1\n"));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "catalogo.csv");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/supplier-catalog/import-csv", content)).StatusCode);
    }
}

/// <summary>The real request limits, on a dedicated server instance with low thresholds.</summary>
public class RateLimitTests : IAsyncLifetime
{
    private CustomWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        _factory = new CustomWebApplicationFactory { RateLimitOverride = (Auth: 5, Pin: 3) };
        await _factory.InitializeDatabaseAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AuthEndpoints_AreLimitedPerClient()
    {
        using var client = _factory.CreateClient();
        var admin = await TestAuth.RegisterAsync(client, "Admin", "rl-admin@test.local", "AdminPass123!"); // 1st auth call

        for (var attempt = 0; attempt < 4; attempt++)                                                       // 2nd-5th
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await TestAuth.LoginRawAsync(client, "rl-admin@test.local", "x")).StatusCode);
        }

        var limited = await TestAuth.LoginRawAsync(client, "rl-admin@test.local", "AdminPass123!");       // 6th
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(admin.Token);
    }

    [Fact]
    public async Task PinIdentification_IsLimitedPerUser()
    {
        using var anonymous = _factory.CreateClient();
        var admin = await TestAuth.RegisterAsync(anonymous, "Admin", "pin-admin@test.local", "AdminPass123!");
        using var client = _factory.AuthenticatedClient(admin.Token);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            Assert.NotEqual(HttpStatusCode.TooManyRequests,
                (await client.PostAsJsonAsync("/api/users/identify-by-pin", new IdentifyByPinRequest($"{1000 + attempt}"))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/users/identify-by-pin", new IdentifyByPinRequest("1999"))).StatusCode);
    }
}
