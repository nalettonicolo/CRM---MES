using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class CarriersTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public CarriersTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<CarrierResponse> CreateCarrierAsync()
    {
        var code = $"CAR-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/carriers", new CreateCarrierRequest($"Corriere {code}", code, "info@example.invalid", "0123456789"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CarrierResponse>())!;
    }

    [Fact]
    public async Task CreateCarrier_ThenList_ReturnsIt()
    {
        var carrier = await CreateCarrierAsync();

        var listResponse = await _adminClient.GetFromJsonAsync<List<CarrierResponse>>("/api/carriers");

        Assert.Contains(listResponse!, c => c.Id == carrier.Id && c.Code == carrier.Code);
    }

    [Fact]
    public async Task CreateCarrier_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP-{Guid.NewGuid():N}"[..12];
        await _adminClient.PostAsJsonAsync("/api/carriers", new CreateCarrierRequest("Primo", code, null, null));

        var response = await _adminClient.PostAsJsonAsync("/api/carriers", new CreateCarrierRequest("Secondo", code, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateCarrier_AsOperator_ReturnsForbidden()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "car-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync(
            "/api/carriers", new CreateCarrierRequest("Non autorizzato", $"OP-{Guid.NewGuid():N}", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCarrierDetail_IncludesItsShipments()
    {
        var carrier = await CreateCarrierAsync();
        var createShipment = await _adminClient.PostAsJsonAsync(
            "/api/shipments", new { direction = "Outbound", carrierId = carrier.Id, counterpartReference = "Cliente Detail Test" });
        createShipment.EnsureSuccessStatusCode();

        var response = await _adminClient.GetAsync($"/api/carriers/{carrier.Id}/detail");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<CarrierDetailResponse>();
        Assert.Equal(carrier.Name, detail!.Name);
        Assert.Single(detail.Shipments);
        Assert.Equal("Cliente Detail Test", detail.Shipments[0].CounterpartReference);
    }

    [Fact]
    public async Task DeactivateCarrier_RemovesItFromActiveList()
    {
        var carrier = await CreateCarrierAsync();

        var deleteResponse = await _adminClient.DeleteAsync($"/api/carriers/{carrier.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _adminClient.GetFromJsonAsync<List<CarrierResponse>>("/api/carriers");
        Assert.DoesNotContain(listResponse!, c => c.Id == carrier.Id);
    }
}
