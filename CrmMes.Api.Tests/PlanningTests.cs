using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

public class PlanningTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public PlanningTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<WorkOrderResponse> CreateWorkOrderWithDueDateAsync(DateTime dueDate, Guid? areaId = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{suffix}", "Prodotto planning", null));
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, areaId, null, dueDate, null));
        return (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
    }

    [Fact]
    public async Task Planning_IncludesWorkOrderWithDueDateInRange_AndExcludesCompletedOnes()
    {
        var inRangeDate = DateTime.UtcNow.Date.AddDays(3);
        var order = await CreateWorkOrderWithDueDateAsync(inRangeDate);

        var completedDate = DateTime.UtcNow.Date.AddDays(2);
        var completedOrder = await CreateWorkOrderWithDueDateAsync(completedDate);
        await _adminClient.PostAsync($"/api/work-orders/{completedOrder.Id}/release", null);
        var detail = await _adminClient.GetFromJsonAsync<WorkOrderResponse>($"/api/work-orders/{completedOrder.Id}");
        foreach (var op in detail!.Operations)
        {
            await _adminClient.PostAsync($"/api/work-orders/{completedOrder.Id}/operations/{op.Id}/start", null);
            await _adminClient.PostAsync($"/api/work-orders/{completedOrder.Id}/operations/{op.Id}/complete", null);
        }
        await _adminClient.PostAsync($"/api/work-orders/{completedOrder.Id}/complete", null);

        var planning = await _adminClient.GetFromJsonAsync<PlanningResponse>("/api/planning?weeks=8");

        Assert.NotNull(planning);
        Assert.Contains(planning!.Entries, e => e.Type == "WorkOrder" && e.Id == order.Id);
        Assert.DoesNotContain(planning.Entries, e => e.Type == "WorkOrder" && e.Id == completedOrder.Id);
    }

    [Fact]
    public async Task Planning_IncludesPurchaseOrderAfterSettingExpectedDelivery()
    {
        var supplierResponse = await _adminClient.PostAsJsonAsync(
            "/api/suppliers", new { name = $"Fornitore {Guid.NewGuid():N}"[..20], code = $"SUP-{Guid.NewGuid():N}"[..10] });
        supplierResponse.EnsureSuccessStatusCode();
        var supplierId = (await supplierResponse.Content.ReadFromJsonAsync<SupplierResponse>())!.Id;

        var materialResponse = await _adminClient.PostAsJsonAsync(
            "/api/materials", new CreateMaterialRequest($"MAT-{Guid.NewGuid():N}"[..12], "Materiale planning", "pz", 0, 0));
        var material = (await materialResponse.Content.ReadFromJsonAsync<MaterialResponse>())!;

        var poResponse = await _adminClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(supplierId, null, [new CreatePurchaseOrderItemRequest(material.Code, 10, null, 1, null)]));
        poResponse.EnsureSuccessStatusCode();
        var order = (await poResponse.Content.ReadFromJsonAsync<PurchaseOrderResponse>())!;

        var expectedDate = DateTime.UtcNow.Date.AddDays(5);
        var setDeliveryResponse = await _adminClient.PutAsJsonAsync(
            $"/api/procurement/purchase-orders/{order.Id}/expected-delivery", new SetExpectedDeliveryRequest(expectedDate));
        Assert.True(setDeliveryResponse.IsSuccessStatusCode);

        var planning = await _adminClient.GetFromJsonAsync<PlanningResponse>("/api/planning?weeks=8");

        Assert.NotNull(planning);
        var entry = Assert.Single(planning!.Entries, e => e.Type == "PurchaseOrder" && e.Id == order.Id);
        Assert.Equal(expectedDate, entry.Date);
    }

    [Fact]
    public async Task Planning_TypeFilter_OnlyReturnsRequestedType()
    {
        await CreateWorkOrderWithDueDateAsync(DateTime.UtcNow.Date.AddDays(1));

        var planning = await _adminClient.GetFromJsonAsync<PlanningResponse>("/api/planning?weeks=8&types=PurchaseOrder");

        Assert.NotNull(planning);
        Assert.DoesNotContain(planning!.Entries, e => e.Type == "WorkOrder");
    }

    [Fact]
    public async Task Planning_DashboardBucketsOverdueAndThisWeekSeparately()
    {
        var overdueOrder = await CreateWorkOrderWithDueDateAsync(DateTime.UtcNow.Date.AddDays(-3));
        var thisWeekOrder = await CreateWorkOrderWithDueDateAsync(DateTime.UtcNow.Date);

        var planning = await _adminClient.GetFromJsonAsync<PlanningResponse>("/api/planning?weeks=8");

        Assert.NotNull(planning);
        var overdueEntry = planning!.Entries.Single(e => e.Id == overdueOrder.Id);
        var thisWeekEntry = planning.Entries.Single(e => e.Id == thisWeekOrder.Id);
        Assert.True(overdueEntry.Date < DateTime.UtcNow.Date);
        Assert.True(thisWeekEntry.Date >= DateTime.UtcNow.Date);
        Assert.True(planning.Dashboard.Overdue >= 1);
    }
}
