using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class MrpTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public MrpTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

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

    [Fact]
    public async Task Run_MultiLevelBom_ExplodesSubassemblyIntoLeafMaterials()
    {
        var leafCode = $"MRP-L-{Guid.NewGuid():N}"[..12];
        (await _admin.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(leafCode, "Foglio lamiera", "pz", 0, 0))).EnsureSuccessStatusCode();

        var subCode = $"MRP-S-{Guid.NewGuid():N}"[..12];
        var sub = (await (await _admin.PostAsJsonAsync("/api/products",
            new CreateProductRequest(subCode, "Sottoassieme", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        (await _admin.PutAsJsonAsync($"api/products/{sub.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(leafCode, 3, null)]))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync($"api/products/{sub.Id}/routing",
            new ReplaceRoutingRequest([new RoutingStepRequest("Salda", null, "Banco", 20)]))).EnsureSuccessStatusCode();

        var top = (await (await _admin.PostAsJsonAsync("/api/products",
            new CreateProductRequest($"P-{Guid.NewGuid():N}"[..12], "Assieme finito", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        (await _admin.PutAsJsonAsync($"api/products/{top.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(subCode, 2, null)]))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync($"api/products/{top.Id}/routing",
            new ReplaceRoutingRequest([new RoutingStepRequest("Montaggio", null, "Banco", 30)]))).EnsureSuccessStatusCode();

        (await _admin.PostAsJsonAsync("/api/work-orders",
            new CreateWorkOrderRequest(top.Id, 4, null, null, null, null, null))).EnsureSuccessStatusCode();

        var run = (await _admin.GetFromJsonAsync<MrpRunResponse>("/api/procurement/mrp"))!;
        var line = Assert.Single(run.Suggestions, s => s.MaterialCode == leafCode);
        Assert.Equal(24m, line.GrossRequirement); // 4 * 2 * 3
        Assert.DoesNotContain(run.Suggestions, s => s.MaterialCode == subCode);
    }

    [Fact]
    public async Task CreateOrders_FromSuggestions_CreatesDraftPurchaseOrders()
    {
        var matCode = $"MRP-O-{Guid.NewGuid():N}"[..12];
        var materialResponse = await (await _admin.PostAsJsonAsync("/api/materials",
            new CreateMaterialRequest(matCode, "Componente ordinabile", "pz", 0, 0))).Content.ReadFromJsonAsync<MaterialResponse>();

        var supplierCode = $"SUP-{Guid.NewGuid():N}"[..12];
        var supplier = (await (await _admin.PostAsJsonAsync("/api/suppliers",
            new CreateSupplierRequest($"Fornitore {supplierCode}", supplierCode, null, null)))
            .Content.ReadFromJsonAsync<SupplierResponse>())!;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.MaterialSuppliers.Add(new MaterialSupplier
            {
                MaterialId = materialResponse!.Id,
                SupplierId = supplier.Id,
                PartNumber = "PN-1",
                UnitPrice = 4.5m,
                LeadTimeDays = 5
            });
            await db.SaveChangesAsync();
        }

        var product = (await (await _admin.PostAsJsonAsync("/api/products",
            new CreateProductRequest($"P-{Guid.NewGuid():N}"[..12], "Prodotto MRP ordini", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        (await _admin.PutAsJsonAsync($"api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(matCode, 2, null)]))).EnsureSuccessStatusCode();
        (await _admin.PutAsJsonAsync($"api/products/{product.Id}/routing",
            new ReplaceRoutingRequest([new RoutingStepRequest("Montaggio", null, "Banco", 15)]))).EnsureSuccessStatusCode();

        var due = DateTime.UtcNow.Date.AddDays(20);
        (await _admin.PostAsJsonAsync("/api/work-orders",
            new CreateWorkOrderRequest(product.Id, 10, null, null, null, due, null))).EnsureSuccessStatusCode();

        var createResponse = await _admin.PostAsJsonAsync("/api/procurement/mrp/create-orders",
            new CreateMrpOrdersRequest(null));
        createResponse.EnsureSuccessStatusCode();
        var create = (await createResponse.Content.ReadFromJsonAsync<CreateMrpOrdersResponse>())!;

        Assert.Equal(1, create!.OrdersCreated);
        var summary = Assert.Single(create.Orders);
        Assert.Equal(supplier.Id, summary.SupplierId);
        Assert.Empty(create.SkippedMaterialCodes);

        var order = (await _admin.GetFromJsonAsync<PurchaseOrderResponse>(
            $"/api/procurement/purchase-orders/{summary.Id}"))!;
        Assert.Equal("Draft", order.Status);
        Assert.Equal(supplier.Id, order.SupplierId);
        var item = Assert.Single(order.Items);
        Assert.Equal(matCode, item.MaterialCode);
        Assert.Equal(20m, item.Quantity);
        Assert.Equal(4.5m, item.UnitPrice);
        Assert.Equal("Componente ordinabile", item.Description);
    }
}
