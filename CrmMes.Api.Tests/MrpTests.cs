using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class MrpTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;

    public MrpTests(AdminSeededApiTestFixture fixture) =>
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);

    [Fact]
    public async Task Run_ExplodesBom_AndSuggestsShortage()
    {
        var matCode = $"MRP-{Guid.NewGuid():N}"[..12];
        (await _admin.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(matCode, "Vite MRP", "pz", 5, 0))).EnsureSuccessStatusCode();

        var product = (await (await _admin.PostAsJsonAsync("/api/products",
            new CreateProductRequest($"P-{Guid.NewGuid():N}"[..12], "Assieme MRP", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        (await _admin.PutAsJsonAsync($"api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(matCode, 3, null)]))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync($"api/products/{product.Id}/routing",
            new ReplaceRoutingRequest([new RoutingStepRequest("Montaggio", null, "Banco", 30)]))).EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync("/api/work-orders",
            new CreateWorkOrderRequest(product.Id, 4, null, null, null, null, null))).EnsureSuccessStatusCode();

        var run = (await _admin.GetFromJsonAsync<MrpRunResponse>("/api/procurement/mrp"))!;
        var line = Assert.Single(run.Suggestions, s => s.MaterialCode == matCode);
        // Gross 4*3=12, stock 5 → net 7
        Assert.Equal(12m, line.GrossRequirement);
        Assert.Equal(5m, line.Stock);
        Assert.Equal(7m, line.NetRequirement);
        Assert.Equal(7m, line.SuggestedQuantity);
        Assert.True(run.MaterialsToOrder >= 1);
    }
}
