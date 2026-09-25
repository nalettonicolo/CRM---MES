using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class ProcurementTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public ProcurementTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<MaterialResponse> CreateMaterialAsync(decimal stock, decimal minStock)
    {
        var code = $"MAT-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest(code, $"Materiale {code}", "pz", stock, minStock));
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

    [Fact]
    public async Task ScanLowStock_CreatesMissingMaterial_AndIsIdempotent()
    {
        var material = await CreateMaterialAsync(stock: 2, minStock: 10);

        var scanResponse = await _adminClient.PostAsync("/api/procurement/low-stock/scan", null);
        scanResponse.EnsureSuccessStatusCode();
        var firstScan = (await scanResponse.Content.ReadFromJsonAsync<LowStockScanResponse>())!;
        Assert.Equal(1, firstScan.MissingMaterialsCreated);

        var lowStock = await _adminClient.GetFromJsonAsync<List<LowStockMaterialResponse>>("/api/procurement/low-stock");
        var entry = lowStock!.Single(item => item.Code == material.Code);
        Assert.Equal(8, entry.SuggestedQuantity);
        Assert.True(entry.AlreadyRequested);

        var secondScan = (await (await _adminClient.PostAsync("/api/procurement/low-stock/scan", null))
            .Content.ReadFromJsonAsync<LowStockScanResponse>())!;
        Assert.Equal(0, secondScan.MissingMaterialsCreated);
    }

    [Fact]
    public async Task PurchaseOrder_ConfirmAndReceiveInFull_UpdatesStockAndResolvesMissingMaterial()
    {
        var material = await CreateMaterialAsync(stock: 2, minStock: 10);
        var supplier = await CreateSupplierAsync();

        await _adminClient.PostAsync("/api/procurement/low-stock/scan", null);
        var missing = (await _adminClient.GetFromJsonAsync<List<MissingMaterialResponse>>("/api/procurement/missing?status=Open"))!
            .Single(m => m.MaterialCode == material.Code);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null,
                [new CreatePurchaseOrderItemRequest(material.Code, missing.Quantity, null, 3.5m, missing.Id)]));
        createResponse.EnsureSuccessStatusCode();
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        Assert.Equal("Draft", order.Status);

        var confirmResponse = await _adminClient.PostAsync($"/api/procurement/purchase-orders/{order.Id}/confirm", null);
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        var receiveResponse = await _adminClient.PostAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}/receive",
            new ReceivePurchaseOrderRequest([new ReceivePurchaseOrderItemRequest(order.Items[0].Id, missing.Quantity)]));
        Assert.Equal(HttpStatusCode.OK, receiveResponse.StatusCode);
        var received = (await receiveResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        Assert.Equal("Received", received.Status);
        Assert.NotNull(received.ReceivedAt);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedMaterial = await db.Materials.SingleAsync(m => m.Code == material.Code);
        Assert.Equal(2 + missing.Quantity, updatedMaterial.Stock);

        var updatedMissing = await db.MissingMaterials.SingleAsync(m => m.Id == missing.Id);
        Assert.Equal("Resolved", updatedMissing.Status);
    }

    [Fact]
    public async Task PurchaseOrder_PartialReceive_SetsPartiallyReceivedAndKeepsRemainder()
    {
        var material = await CreateMaterialAsync(stock: 0, minStock: 0);
        var supplier = await CreateSupplierAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null,
                [new CreatePurchaseOrderItemRequest(material.Code, 10, null, 1m, null)]));
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        await _adminClient.PostAsync($"/api/procurement/purchase-orders/{order.Id}/confirm", null);

        var receiveResponse = await _adminClient.PostAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}/receive",
            new ReceivePurchaseOrderRequest([new ReceivePurchaseOrderItemRequest(order.Items[0].Id, 4)]));
        var partial = (await receiveResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        Assert.Equal("PartiallyReceived", partial.Status);
        Assert.Equal(4, partial.Items[0].ReceivedQuantity);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var updatedMaterial = await db.Materials.SingleAsync(m => m.Code == material.Code);
        Assert.Equal(4, updatedMaterial.Stock);
    }

    [Fact]
    public async Task ReceivePurchaseOrder_MoreThanOrdered_ReturnsConflict()
    {
        var material = await CreateMaterialAsync(stock: 0, minStock: 0);
        var supplier = await CreateSupplierAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null,
                [new CreatePurchaseOrderItemRequest(material.Code, 5, null, 1m, null)]));
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        await _adminClient.PostAsync($"/api/procurement/purchase-orders/{order.Id}/confirm", null);

        var receiveResponse = await _adminClient.PostAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}/receive",
            new ReceivePurchaseOrderRequest([new ReceivePurchaseOrderItemRequest(order.Items[0].Id, 999)]));

        Assert.Equal(HttpStatusCode.Conflict, receiveResponse.StatusCode);
    }

    [Fact]
    public async Task CancelPurchaseOrder_BeforeReceiving_ReopensLinkedMissingMaterial()
    {
        var material = await CreateMaterialAsync(stock: 2, minStock: 10);
        var supplier = await CreateSupplierAsync();

        await _adminClient.PostAsync("/api/procurement/low-stock/scan", null);
        var missing = (await _adminClient.GetFromJsonAsync<List<MissingMaterialResponse>>("/api/procurement/missing?status=Open"))!
            .Single(m => m.MaterialCode == material.Code);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null,
                [new CreatePurchaseOrderItemRequest(material.Code, missing.Quantity, null, 1m, missing.Id)]));
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var missingAfterOrder = await db.MissingMaterials.SingleAsync(m => m.Id == missing.Id);
            Assert.Equal("Ordered", missingAfterOrder.Status);
        }

        var cancelResponse = await _adminClient.PostAsync($"/api/procurement/purchase-orders/{order.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancelResponse.StatusCode);

        using var verifyScope = _fixture.Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var missingAfterCancel = await verifyDb.MissingMaterials.SingleAsync(m => m.Id == missing.Id);
        Assert.Equal("Open", missingAfterCancel.Status);
    }

    [Fact]
    public async Task EditOrder_WhileDraft_ReplacesItemsAndSupplier()
    {
        var materialA = await CreateMaterialAsync(stock: 0, minStock: 0);
        var supplierA = await CreateSupplierAsync();
        var supplierB = await CreateSupplierAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplierA.Id, null,
                [new CreatePurchaseOrderItemRequest(materialA.Code, 5, null, 1m, null)]));
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        var materialB = await CreateMaterialAsync(stock: 0, minStock: 0);
        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}",
            new EditPurchaseOrderRequest(supplierB.Id,
                [new CreatePurchaseOrderItemRequest(materialB.Code, 9, null, 2.5m, null)]));
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var edited = (await editResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        Assert.Equal(supplierB.Id, edited.SupplierId);
        Assert.Single(edited.Items);
        Assert.Equal(materialB.Code, edited.Items[0].MaterialCode);
        Assert.Equal(9, edited.Items[0].Quantity);
    }

    [Fact]
    public async Task EditOrder_AfterConfirm_ReturnsConflict()
    {
        var material = await CreateMaterialAsync(stock: 0, minStock: 0);
        var supplier = await CreateSupplierAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null,
                [new CreatePurchaseOrderItemRequest(material.Code, 5, null, 1m, null)]));
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        await _adminClient.PostAsync($"/api/procurement/purchase-orders/{order.Id}/confirm", null);

        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}",
            new EditPurchaseOrderRequest(supplier.Id, [new CreatePurchaseOrderItemRequest(material.Code, 9, null, 1m, null)]));

        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
    }

    [Fact]
    public async Task EditOrder_DroppingMissingMaterialLink_ReopensIt()
    {
        var material = await CreateMaterialAsync(stock: 2, minStock: 10);
        var supplier = await CreateSupplierAsync();

        await _adminClient.PostAsync("/api/procurement/low-stock/scan", null);
        var missing = (await _adminClient.GetFromJsonAsync<List<MissingMaterialResponse>>("/api/procurement/missing?status=Open"))!
            .Single(m => m.MaterialCode == material.Code);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null,
                [new CreatePurchaseOrderItemRequest(material.Code, missing.Quantity, null, 1m, missing.Id)]));
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        var otherMaterial = await CreateMaterialAsync(stock: 0, minStock: 0);
        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}",
            new EditPurchaseOrderRequest(supplier.Id, [new CreatePurchaseOrderItemRequest(otherMaterial.Code, 1, null, 1m, null)]));
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var missingAfterEdit = await db.MissingMaterials.SingleAsync(m => m.Id == missing.Id);
        Assert.Equal("Open", missingAfterEdit.Status);
    }

    [Fact]
    public async Task SetExpectedDelivery_AppearsInBothDetailAndListEndpoints()
    {
        var material = await CreateMaterialAsync(stock: 10, minStock: 0);
        var supplier = await CreateSupplierAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplier.Id, null,
                [new CreatePurchaseOrderItemRequest(material.Code, 5, null, 1m, null)]));
        var order = (await createResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        Assert.Null(order.ExpectedDeliveryDate);

        var expectedDate = DateTime.UtcNow.Date.AddDays(10);
        var setResponse = await _adminClient.PutAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}/expected-delivery", new SetExpectedDeliveryRequest(expectedDate));
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        var updated = (await setResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;
        Assert.Equal(expectedDate, updated.ExpectedDeliveryDate);

        var detail = await _adminClient.GetFromJsonAsync<PurchaseOrderResponse>($"/api/procurement/purchase-orders/{order.Id}");
        Assert.Equal(expectedDate, detail!.ExpectedDeliveryDate);

        var list = await _adminClient.GetFromJsonAsync<List<PurchaseOrderSummaryResponse>>("/api/procurement/purchase-orders");
        var listEntry = list!.Single(o => o.Id == order.Id);
        Assert.Equal(expectedDate, listEntry.ExpectedDeliveryDate);
    }
}
