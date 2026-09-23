using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class AuthTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public AuthTests(AdminSeededApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Register_AfterBootstrapUserExists_ReturnsForbidden()
    {
        // La registrazione pubblica è bootstrap-only (vedi AuthBootstrapTests): il fixture ha già
        // registrato l'Admin di bootstrap in InitializeAsync, quindi qualunque ulteriore tentativo di
        // auto-registrazione va rifiutato — solo un Admin può creare altri utenti, da POST /api/users.
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Due", "duplicato@test.local", "Password123!"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "login-ok");

        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var response = await TestAuth.LoginRawAsync(_fixture.Client, AdminSeededApiTestFixture.AdminEmail, "WrongPassword!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        var response = await TestAuth.LoginRawAsync(_fixture.Client, "non-esiste@test.local", "Password123!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
