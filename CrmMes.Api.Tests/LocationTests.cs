using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

public class LocationTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;

    public LocationTests(AdminSeededApiTestFixture fixture) =>
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);

    private async Task<MaterialResponse> CreateMaterialAsync(decimal stock = 0)
    {
        var code = $"LOC-{Guid.NewGuid():N}"[..12];
        var response = await _admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, $"Mat {code}", "pz", stock, 0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>())!;
    }

    [Fact]
    public async Task CreateLocation_AdjustStock_UpdatesLocationAndMaterialTotals()
    {
        var material = await CreateMaterialAsync(stock: 10);
        var locCode = $"UB-{Guid.NewGuid():N}"[..10];

        var createLoc = await _admin.PostAsJsonAsync("/api/locations", new CreateWarehouseLocationRequest("Scaffale A1", locCode));
        createLoc.EnsureSuccessStatusCode();
        var location = (await createLoc.Content.ReadFromJsonAsync<WarehouseLocationResponse>())!;

        var adjust = await _admin.PostAsJsonAsync(
            $"/api/locations/{location.Id}/stock/adjust",
            new AdjustLocationStockRequest(material.Id, 7, "Carico iniziale"));
        adjust.EnsureSuccessStatusCode();

        var stock = await _admin.GetFromJsonAsync<List<LocationStockResponse>>($"/api/locations/{location.Id}/stock");
        var row = Assert.Single(stock!);
        Assert.Equal(7, row.Quantity);
        Assert.Equal(material.Id, row.MaterialId);

        var matAfter = await _admin.GetFromJsonAsync<MaterialResponse>($"/api/materials/{material.Id}");
        Assert.Equal(17, matAfter!.Stock);

        var adjustDown = await _admin.PostAsJsonAsync(
            $"/api/locations/{location.Id}/stock/adjust",
            new AdjustLocationStockRequest(material.Id, 5, "Rettifica"));
        adjustDown.EnsureSuccessStatusCode();

        matAfter = await _admin.GetFromJsonAsync<MaterialResponse>($"/api/materials/{material.Id}");
        Assert.Equal(15, matAfter!.Stock);
    }

    [Fact]
    public async Task InventoryClose_AppliesCountedDifferences_ToLocationAndMaterial()
    {
        var material = await CreateMaterialAsync(stock: 0);
        var locCode = $"INV-{Guid.NewGuid():N}"[..10];

        var createLoc = await _admin.PostAsJsonAsync("/api/locations", new CreateWarehouseLocationRequest("Area inventario", locCode));
        createLoc.EnsureSuccessStatusCode();
        var location = (await createLoc.Content.ReadFromJsonAsync<WarehouseLocationResponse>())!;

        (await _admin.PostAsJsonAsync(
            $"/api/locations/{location.Id}/stock/adjust",
            new AdjustLocationStockRequest(material.Id, 20, "Giacenza"))).EnsureSuccessStatusCode();

        var open = await _admin.PostAsJsonAsync("/api/locations/inventory", new OpenInventorySessionRequest(null, "Test"));
        open.EnsureSuccessStatusCode();
        var session = (await open.Content.ReadFromJsonAsync<InventorySessionResponse>())!;

        (await _admin.PostAsJsonAsync(
            $"/api/locations/inventory/{session.Id}/lines",
            new UpsertInventoryLineRequest(location.Id, material.Id, 18))).EnsureSuccessStatusCode();

        var closed = await _admin.PostAsync($"/api/locations/inventory/{session.Id}/close", null);
        closed.EnsureSuccessStatusCode();
        var result = (await closed.Content.ReadFromJsonAsync<InventorySessionResponse>())!;

        Assert.Equal(InventorySessionStatuses.Closed, result.Status);
        Assert.NotNull(result.ClosedAt);
        var line = Assert.Single(result.Lines);
        Assert.Equal(20, line.SystemQuantity);
        Assert.Equal(18, line.CountedQuantity);
        Assert.Equal(-2, line.Difference);

        var stock = await _admin.GetFromJsonAsync<List<LocationStockResponse>>($"/api/locations/{location.Id}/stock");
        Assert.Equal(18, Assert.Single(stock!).Quantity);

        var matAfter = await _admin.GetFromJsonAsync<MaterialResponse>($"/api/materials/{material.Id}");
        Assert.Equal(18, matAfter!.Stock);
    }
}
