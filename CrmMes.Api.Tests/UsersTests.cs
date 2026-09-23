using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class UsersTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public UsersTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<UserResponse> CreateOperatorAsync()
    {
        var email = $"pin-{Guid.NewGuid():N}@test.local";
        var response = await _adminClient.PostAsJsonAsync(
            "/api/users", new CreateUserRequest("Operatore Pin", email, "Password123!", "Operator"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserResponse>())!;
    }

    [Fact]
    public async Task SetUserPin_ThenIdentify_ReturnsMatchingOperator()
    {
        var user = await CreateOperatorAsync();
        var setResponse = await _adminClient.PutAsJsonAsync($"/api/users/{user.Id}/pin", new SetUserPinRequest("4321"));
        Assert.Equal(HttpStatusCode.NoContent, setResponse.StatusCode);

        var identifyResponse = await _adminClient.PostAsJsonAsync("/api/users/identify-by-pin", new IdentifyByPinRequest("4321"));

        Assert.Equal(HttpStatusCode.OK, identifyResponse.StatusCode);
        var identified = await identifyResponse.Content.ReadFromJsonAsync<IdentifyByPinResponse>();
        Assert.Equal(user.Id, identified!.Id);
        Assert.Equal("Operatore Pin", identified.Name);
    }

    [Fact]
    public async Task SetUserPin_NonNumericOrWrongLength_ReturnsBadRequest()
    {
        var user = await CreateOperatorAsync();

        var tooShort = await _adminClient.PutAsJsonAsync($"/api/users/{user.Id}/pin", new SetUserPinRequest("12"));
        var nonNumeric = await _adminClient.PutAsJsonAsync($"/api/users/{user.Id}/pin", new SetUserPinRequest("abcd"));

        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, nonNumeric.StatusCode);
    }

    [Fact]
    public async Task SetUserPin_AsOperator_ReturnsForbidden()
    {
        var user = await CreateOperatorAsync();
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "pin-set");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PutAsJsonAsync($"/api/users/{user.Id}/pin", new SetUserPinRequest("9999"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task IdentifyByPin_WrongPin_ReturnsNotFound()
    {
        var user = await CreateOperatorAsync();
        await _adminClient.PutAsJsonAsync($"/api/users/{user.Id}/pin", new SetUserPinRequest("1111"));

        var response = await _adminClient.PostAsJsonAsync("/api/users/identify-by-pin", new IdentifyByPinRequest("9999"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IdentifyByPin_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymousClient = _fixture.Factory.CreateClient();

        var response = await anonymousClient.PostAsJsonAsync("/api/users/identify-by-pin", new IdentifyByPinRequest("1234"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
