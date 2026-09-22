using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class MaterialLotsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public MaterialLotsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<MaterialResponse> CreateMaterialAsync(decimal stock)
    {
        var code = $"MAT-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/materials", new CreateMaterialRequest(code, $"Materiale {code}", "pz", stock, 0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>())!;
    }

    private async Task<SupplierResponse> CreateSupplierAsync()
    {
        var code = $"SUP-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest($"Fornitore {code}", code, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<SupplierResponse>())!;
    }

    private async Task<Area> CreateAreaAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {suffix}", $"AREA-{suffix}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Area>())!;
    }

    [Fact]
    public async Task CreateMaterial_WithOpeningStock_CreatesInitialLot()
    {
        var material = await CreateMaterialAsync(stock: 25);

        var lots = await _adminClient.GetFromJsonAsync<List<MaterialLotSummaryResponse>>($"/api/material-lots?materialCode={material.Code}");

        var lot = Assert.Single(lots!);
        Assert.Equal(25, lot.Quantity);
        Assert.Equal(25, lot.InitialQuantity);
        Assert.StartsWith("INIZIALE-", lot.LotNumber);
    }

    [Fact]
    public async Task CreateMaterial_WithZeroOpeningStock_CreatesNoLot()
    {
        var material = await CreateMaterialAsync(stock: 0);

        var lots = await _adminClient.GetFromJsonAsync<List<MaterialLotSummaryResponse>>($"/api/material-lots?materialCode={material.Code}");

        Assert.Empty(lots!);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_WithExplicitLotNumber_CreatesTraceableLot()
    {
        var material = await CreateMaterialAsync(stock: 0);
        var supplier = await CreateSupplierAsync();

        var orderResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null, [new CreatePurchaseOrderItemRequest(material.Code, 10, null, 1m, null)]));
        var order = (await orderResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        await _adminClient.PostAsync($"/api/procurement/purchase-orders/{order.Id}/confirm", null);

        var receiveResponse = await _adminClient.PostAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}/receive",
            new ReceivePurchaseOrderRequest([new ReceivePurchaseOrderItemRequest(order.Items[0].Id, 10, "FORNITORE-LOTTO-42")]));
        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);

        var lots = await _adminClient.GetFromJsonAsync<List<MaterialLotSummaryResponse>>($"/api/material-lots?materialCode={material.Code}");
        var lot = Assert.Single(lots!);
        Assert.Equal("FORNITORE-LOTTO-42", lot.LotNumber);
        Assert.Equal(10, lot.Quantity);
        Assert.Equal(order.SupplierId, lot.SupplierId);
        Assert.Equal(order.Id, lot.PurchaseOrderId);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_WithoutLotNumber_AutoGeneratesOne()
    {
        var material = await CreateMaterialAsync(stock: 0);
        var supplier = await CreateSupplierAsync();

        var orderResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null, [new CreatePurchaseOrderItemRequest(material.Code, 5, null, 1m, null)]));
        var order = (await orderResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        await _adminClient.PostAsync($"/api/procurement/purchase-orders/{order.Id}/confirm", null);

        await _adminClient.PostAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}/receive",
            new ReceivePurchaseOrderRequest([new ReceivePurchaseOrderItemRequest(order.Items[0].Id, 5)]));

        var lots = await _adminClient.GetFromJsonAsync<List<MaterialLotSummaryResponse>>($"/api/material-lots?materialCode={material.Code}");
        var lot = Assert.Single(lots!);
        Assert.StartsWith("LOT-", lot.LotNumber);
    }

    [Fact]
    public async Task CloseWithdrawalSlip_ConsumesOldestLotFirst_AndRecordsGenealogy()
    {
        var material = await CreateMaterialAsync(stock: 0);
        var area = await CreateAreaAsync();

        // First lot: 3 units, created first so it must be drained before the second lot is touched.
        var firstLotResponse = await _adminClient.PostAsJsonAsync(
            "/api/material-lots", new CreateMaterialLotRequest(material.Code, "LOTTO-VECCHIO", 3, null));
        firstLotResponse.EnsureSuccessStatusCode();
        var firstLot = (await firstLotResponse.Content.ReadFromJsonAsync<MaterialLotSummaryResponse>())!;

        await _adminClient.PostAsJsonAsync(
            "/api/material-lots", new CreateMaterialLotRequest(material.Code, "LOTTO-NUOVO", 10, null));

        var slipResponse = await _adminClient.PostAsJsonAsync(
            "/api/withdrawal-slips",
            new CreateWithdrawalSlipRequest(
                area.Id, _fixture.Admin.UserId, null, null,
                [new CreateWithdrawalSlipItemRequest(material.Code, 5, null, null)]));
        var slip = (await slipResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;

        var closeResponse = await _adminClient.PostAsync($"/api/withdrawal-slips/{slip.Id}/close", null);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var lots = (await _adminClient.GetFromJsonAsync<List<MaterialLotSummaryResponse>>($"/api/material-lots?materialCode={material.Code}"))!;
        var oldLot = lots.Single(l => l.LotNumber == "LOTTO-VECCHIO");
        var newLot = lots.Single(l => l.LotNumber == "LOTTO-NUOVO");
        Assert.Equal(0, oldLot.Quantity);
        Assert.Equal(8, newLot.Quantity);

        var oldLotDetail = await _adminClient.GetFromJsonAsync<MaterialLotDetailResponse>($"/api/material-lots/{firstLot.Id}");
        var usage = Assert.Single(oldLotDetail!.Usages);
        Assert.Equal(3, usage.Quantity);
        Assert.Equal(slip.Id, usage.WithdrawalSlipId);
    }

    [Fact]
    public async Task CreateWorkOrder_GetsAutoGeneratedProductLotNumber()
    {
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{Guid.NewGuid():N}"[..12], "Prodotto lotti", null));
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var orderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await orderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        Assert.StartsWith("LOT-", order.ProductLotNumber);
    }

    [Fact]
    public async Task WorkOrder_MaterialLotsEndpoint_ReflectsConsumptionAfterSlipClosed()
    {
        var material = await CreateMaterialAsync(stock: 0);
        var area = await CreateAreaAsync();
        await _adminClient.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(material.Code, "LOTTO-WO", 100, null));

        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{Guid.NewGuid():N}"[..12], "Prodotto tracciato", null));
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;
        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(material.Code, 2, null)]));

        var orderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 3, null, area.Id, null, null, null));
        var order = (await orderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var generateResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/generate-withdrawal-slip", null);
        var generated = (await generateResponse.Content.ReadFromJsonAsync<WorkOrderWithdrawalSlipResponse>())!;
        await _adminClient.PostAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}/close", null);

        var consumedLots = await _adminClient.GetFromJsonAsync<List<WorkOrderMaterialLotResponse>>($"/api/work-orders/{order.Id}/material-lots");
        var consumed = Assert.Single(consumedLots!);
        Assert.Equal("LOTTO-WO", consumed.LotNumber);
        Assert.Equal(6, consumed.QuantityConsumed);
        Assert.Equal(generated.WithdrawalSlipId, consumed.WithdrawalSlipId);
    }

    [Fact]
    public async Task CreateMaterialLot_DuplicateLotNumberForSameMaterial_ReturnsConflict()
    {
        var material = await CreateMaterialAsync(stock: 0);
        await _adminClient.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(material.Code, "DUP-LOT", 1, null));

        var response = await _adminClient.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(material.Code, "DUP-LOT", 1, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterialLot_AsOperator_ReturnsForbidden()
    {
        var material = await CreateMaterialAsync(stock: 0);
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "lot-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(material.Code, "OP-LOT", 1, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
