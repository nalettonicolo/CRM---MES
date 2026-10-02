using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

public class CapaTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;
    private readonly AdminSeededApiTestFixture _fixture;

    public CapaTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public async Task CreateCapa_AndClose_UpdatesStatus()
    {
        var create = await _admin.PostAsJsonAsync("/api/capas", new CreateCapaRequest(null, null, "Azione test", "Descrizione", null, null, null, null));
        create.EnsureSuccessStatusCode();
        var capa = (await create.Content.ReadFromJsonAsync<CapaResponse>())!;
        Assert.Equal(CapaStatuses.Open, capa.Status);
        Assert.StartsWith("CAPA-", capa.Code);

        var close = await _admin.PutAsJsonAsync($"/api/capas/{capa.Id}/status", new UpdateCapaStatusRequest(CapaStatuses.Closed));
        close.EnsureSuccessStatusCode();
        var closed = (await close.Content.ReadFromJsonAsync<CapaResponse>())!;
        Assert.Equal(CapaStatuses.Closed, closed.Status);
        Assert.NotNull(closed.ClosedAt);
    }

    [Fact]
    public async Task Operator_CannotCreateCapa()
    {
        var auth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "capa-op");
        using var client = _fixture.Factory.AuthenticatedClient(auth.Token);
        var response = await client.PostAsJsonAsync("/api/capas", new CreateCapaRequest("CAPA-X", null, "Test", null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
