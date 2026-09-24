using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class WithdrawalSlipsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public WithdrawalSlipsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<(Area Area, MaterialResponse Material)> SeedAreaAndMaterialAsync(decimal stock, decimal minStock = 0)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var areaResponse = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {suffix}", $"AREA-{suffix}"));
        areaResponse.EnsureSuccessStatusCode();
        var area = (await areaResponse.Content.ReadFromJsonAsync<Area>())!;

        var materialResponse = await _adminClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest($"MAT-{suffix}", $"Materiale {suffix}", "pz", stock, minStock));
        materialResponse.EnsureSuccessStatusCode();
        var material = (await materialResponse.Content.ReadFromJsonAsync<MaterialResponse>())!;

        return (area, material);
    }

    [Fact]
    public async Task CreateSlip_WithSufficientStock_IsNotMissingAndReachesClosed()
    {
        var (area, material) = await SeedAreaAndMaterialAsync(stock: 20);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/withdrawal-slips",
            new CreateWithdrawalSlipRequest(
                area.Id,
                _fixture.Admin.UserId,
                null,
                null,
                [new CreateWithdrawalSlipItemRequest(material.Code, 5, null, null)]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var slip = (await createResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;
        Assert.False(slip.Items.Single().IsMissing);
        Assert.Equal("Draft", slip.Status);

        var readyResponse = await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/ready", null);
        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);

        var closeResponse = await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        var closed = (await closeResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;
        Assert.Equal("Closed", closed.Status);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedMaterial = await db.Materials.SingleAsync(m => m.Code == material.Code);
        Assert.Equal(15, updatedMaterial.Stock);

        var auditActions = await db.AuditLogs
            .Where(log => log.EntityId == slip.Id)
            .Select(log => log.Action)
            .ToListAsync();
        Assert.Contains("WithdrawalSlipCreated", auditActions);
        Assert.Contains("WithdrawalSlipReady", auditActions);
        Assert.Contains("WithdrawalSlipClosed", auditActions);
    }

    [Fact]
    public async Task CreateSlip_WithInsufficientStock_IsFlaggedMissingAndBlocksReady()
    {
        var (area, material) = await SeedAreaAndMaterialAsync(stock: 2);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/withdrawal-slips",
            new CreateWithdrawalSlipRequest(
                area.Id,
                _fixture.Admin.UserId,
                null,
                null,
                [new CreateWithdrawalSlipItemRequest(material.Code, 10, null, null)]));
        var slip = (await createResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;
        Assert.True(slip.Items.Single().IsMissing);

        var readyResponse = await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/ready", null);
        Assert.Equal(HttpStatusCode.Conflict, readyResponse.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var missing = await db.MissingMaterials.SingleOrDefaultAsync(m => m.WithdrawalSlipId == slip.Id);
        Assert.NotNull(missing);
        Assert.Equal("Open", missing!.Status);
    }

    [Fact]
    public async Task CancelSlip_ThenClose_IsBlocked()
    {
        var (area, material) = await SeedAreaAndMaterialAsync(stock: 20);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/withdrawal-slips",
            new CreateWithdrawalSlipRequest(
                area.Id,
                _fixture.Admin.UserId,
                null,
                null,
                [new CreateWithdrawalSlipItemRequest(material.Code, 1, null, null)]));
        var slip = (await createResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;

        var cancelResponse = await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        // A cancelled slip must never be closeable: closing decrements stock, which would be wrong here.
        var closeResponse = await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/close", null);
        Assert.Equal(HttpStatusCode.Conflict, closeResponse.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedMaterial = await db.Materials.SingleAsync(m => m.Code == material.Code);
        Assert.Equal(20, updatedMaterial.Stock);

        var auditActions = await db.AuditLogs
            .Where(log => log.EntityId == slip.Id)
            .Select(log => log.Action)
            .ToListAsync();
        Assert.Contains("WithdrawalSlipCancelled", auditActions);
        Assert.DoesNotContain("WithdrawalSlipClosed", auditActions);
    }

    [Fact]
    public async Task EditSlip_WhileDraft_ReplacesItemsEntirely()
    {
        var (area, materialA) = await SeedAreaAndMaterialAsync(stock: 20);
        var materialBResponse = await _adminClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest($"MAT-{Guid.NewGuid():N}"[..12], "Secondo materiale", "pz", 20, 0));
        var materialB = (await materialBResponse.Content.ReadFromJsonAsync<MaterialResponse>())!;

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/withdrawal-slips",
            new CreateWithdrawalSlipRequest(
                area.Id, _fixture.Admin.UserId, null, "note originale",
                [new CreateWithdrawalSlipItemRequest(materialA.Code, 5, null, null)]));
        var slip = (await createResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;

        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/withdrawal-slips/{slip.Id}",
            new EditWithdrawalSlipRequest("nota aggiornata", [new CreateWithdrawalSlipItemRequest(materialB.Code, 3, null, null)]));
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var edited = (await editResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;

        Assert.Single(edited.Items);
        Assert.Equal(materialB.Code, edited.Items[0].MaterialCode);
        Assert.Equal("nota aggiornata", edited.Notes);
    }

    [Fact]
    public async Task EditSlip_RecomputesMissingFlagAndCreatesMissingMaterial()
    {
        var (area, material) = await SeedAreaAndMaterialAsync(stock: 2);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/withdrawal-slips",
            new CreateWithdrawalSlipRequest(
                area.Id, _fixture.Admin.UserId, null, null,
                [new CreateWithdrawalSlipItemRequest(material.Code, 1, null, null)]));
        var slip = (await createResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;
        Assert.False(slip.Items[0].IsMissing);

        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/withdrawal-slips/{slip.Id}",
            new EditWithdrawalSlipRequest(null, [new CreateWithdrawalSlipItemRequest(material.Code, 50, null, null)]));
        var edited = (await editResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;

        Assert.True(edited.Items[0].IsMissing);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var missing = await db.MissingMaterials.SingleOrDefaultAsync(m => m.WithdrawalSlipId == slip.Id);
        Assert.NotNull(missing);
    }

    [Fact]
    public async Task EditSlip_AfterReady_ReturnsConflict()
    {
        var (area, material) = await SeedAreaAndMaterialAsync(stock: 20);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/withdrawal-slips",
            new CreateWithdrawalSlipRequest(
                area.Id, _fixture.Admin.UserId, null, null,
                [new CreateWithdrawalSlipItemRequest(material.Code, 1, null, null)]));
        var slip = (await createResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;
        await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/ready", null);

        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/withdrawal-slips/{slip.Id}",
            new EditWithdrawalSlipRequest(null, [new CreateWithdrawalSlipItemRequest(material.Code, 2, null, null)]));

        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
    }

    /// <summary>Product with a one-line BOM (2 units of the given material per product unit) and a
    /// single-step routing, ready to drive a work order with tracked units end to end.</summary>
    private async Task<ProductResponse> CreateProductWithBomAsync(string materialCode)
    {
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{Guid.NewGuid():N}"[..12], "Prodotto di test", null));
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(materialCode, 2, null)]));
        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/routing",
            new ReplaceRoutingRequest([new RoutingStepRequest("Assemblaggio", null, "Linea 1", 30)]));

        return product;
    }

    [Fact]
    public async Task CloseWithdrawalSlip_ForWorkOrderWithUnits_AttributesOneLotAcrossAllUnits()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var materialResponse = await _adminClient.PostAsJsonAsync(
            "/api/materials", new CreateMaterialRequest($"MAT-{suffix}", $"Materiale {suffix}", "pz", 0, 0));
        var material = (await materialResponse.Content.ReadFromJsonAsync<MaterialResponse>())!;

        var lotResponse = await _adminClient.PostAsJsonAsync(
            "/api/material-lots", new CreateMaterialLotRequest(material.Code, $"LOT-{suffix}", 10, null));
        lotResponse.EnsureSuccessStatusCode();
        var lot = (await lotResponse.Content.ReadFromJsonAsync<MaterialLotSummaryResponse>())!;

        var product = await CreateProductWithBomAsync(material.Code);
        var areaResponse = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {suffix}", $"AREA-{suffix}"));
        var area = (await areaResponse.Content.ReadFromJsonAsync<Area>())!;

        // 3 units, BOM needs 2 each -> 6 total, comfortably inside the single 10-unit lot.
        var orderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 3, null, area.Id, null, null, null));
        var order = (await orderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var generateResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/generate-withdrawal-slip", null);
        var generated = (await generateResponse.Content.ReadFromJsonAsync<WorkOrderWithdrawalSlipResponse>())!;
        await _adminClient.PostAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}/ready", null);
        var closeResponse = await _adminClient.PostAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units");
        Assert.Equal(3, units!.Count);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var unit in units)
        {
            var attributed = await db.WorkOrderUnitMaterialLots
                .Where(l => l.WorkOrderUnitId == unit.Id)
                .ToListAsync();
            Assert.Single(attributed);
            Assert.Equal(lot.Id, attributed[0].MaterialLotId);
            Assert.Equal(2, attributed[0].Quantity);
        }
    }

    [Fact]
    public async Task CloseWithdrawalSlip_ForWorkOrderWithUnits_SplitsAUnitAcrossTwoLotsWhenTheBoundaryFallsMidUnit()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var materialResponse = await _adminClient.PostAsJsonAsync(
            "/api/materials", new CreateMaterialRequest($"MAT-{suffix}", $"Materiale {suffix}", "pz", 0, 0));
        var material = (await materialResponse.Content.ReadFromJsonAsync<MaterialResponse>())!;

        // Two lots of 3 each, received in sequence (FIFO order = lot1 then lot2). BOM needs 2 per unit
        // over 3 units (6 total): unit1 takes 2 from lot1 (1 left), unit2 takes the last 1 from lot1 +
        // 1 from lot2 (a genuine split), unit3 takes the remaining 2 from lot2.
        var lot1Response = await _adminClient.PostAsJsonAsync(
            "/api/material-lots", new CreateMaterialLotRequest(material.Code, $"LOT-{suffix}-A", 3, null));
        var lot1 = (await lot1Response.Content.ReadFromJsonAsync<MaterialLotSummaryResponse>())!;
        var lot2Response = await _adminClient.PostAsJsonAsync(
            "/api/material-lots", new CreateMaterialLotRequest(material.Code, $"LOT-{suffix}-B", 3, null));
        var lot2 = (await lot2Response.Content.ReadFromJsonAsync<MaterialLotSummaryResponse>())!;

        var product = await CreateProductWithBomAsync(material.Code);
        var areaResponse = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {suffix}", $"AREA-{suffix}"));
        var area = (await areaResponse.Content.ReadFromJsonAsync<Area>())!;

        var orderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 3, null, area.Id, null, null, null));
        var order = (await orderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var generateResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/generate-withdrawal-slip", null);
        var generated = (await generateResponse.Content.ReadFromJsonAsync<WorkOrderWithdrawalSlipResponse>())!;
        await _adminClient.PostAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}/ready", null);
        await _adminClient.PostAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}/close", null);

        var units = (await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units"))!
            .OrderBy(u => u.SequenceNumber).ToList();

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var unit2Lots = await db.WorkOrderUnitMaterialLots.Where(l => l.WorkOrderUnitId == units[1].Id).ToListAsync();
        Assert.Equal(2, unit2Lots.Count);
        Assert.Equal(2, unit2Lots.Sum(l => l.Quantity));
        Assert.Contains(unit2Lots, l => l.MaterialLotId == lot1.Id);
        Assert.Contains(unit2Lots, l => l.MaterialLotId == lot2.Id);

        var unit1Lots = await db.WorkOrderUnitMaterialLots.Where(l => l.WorkOrderUnitId == units[0].Id).ToListAsync();
        Assert.Single(unit1Lots);
        Assert.Equal(lot1.Id, unit1Lots[0].MaterialLotId);

        var unit3Lots = await db.WorkOrderUnitMaterialLots.Where(l => l.WorkOrderUnitId == units[2].Id).ToListAsync();
        Assert.Single(unit3Lots);
        Assert.Equal(lot2.Id, unit3Lots[0].MaterialLotId);
    }
}
