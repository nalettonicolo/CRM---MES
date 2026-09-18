using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class AuthTests : IClassFixture<ApiTestFixture>
{
    private readonly ApiTestFixture _fixture;

    public AuthTests(ApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        await TestAuth.RegisterAsync(_fixture.Client, "Uno", "duplicato@test.local", "Password123!");

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Due", "duplicato@test.local", "Password123!"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Register_ShortPassword_ReturnsBadRequest()
    {
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterRequest("Corto", "corto@test.local", "1234"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        await TestAuth.RegisterAsync(_fixture.Client, "Login Ok", "login-ok@test.local", "Password123!");

        var auth = await TestAuth.LoginAsync(_fixture.Client, "login-ok@test.local", "Password123!");

        Assert.False(string.IsNullOrWhiteSpace(auth.Token));
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        await TestAuth.RegisterAsync(_fixture.Client, "Login Bad", "login-bad@test.local", "Password123!");

        var response = await TestAuth.LoginRawAsync(_fixture.Client, "login-bad@test.local", "WrongPassword!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        var response = await TestAuth.LoginRawAsync(_fixture.Client, "non-esiste@test.local", "Password123!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
