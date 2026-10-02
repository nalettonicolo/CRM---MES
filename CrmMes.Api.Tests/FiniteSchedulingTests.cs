using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

public class FiniteSchedulingTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _adminClient;

    public FiniteSchedulingTests(AdminSeededApiTestFixture fixture)
    {
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<MaterialResponse> CreateMaterialAsync(decimal stock)
    {
        var code = $"MAT-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest(code, $"Materiale {code}", "pz", stock, 0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>())!;
    }

    private async Task CreateWorkCenterAsync(string name, decimal dailyCapacityMinutes)
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/api/work-centers",
            new CreateWorkCenterRequest($"WC-{Guid.NewGuid():N}"[..12], name, null, dailyCapacityMinutes));
        response.EnsureSuccessStatusCode();
    }

    private async Task<ProductResponse> CreateProductWithRoutingAsync(
        decimal materialStock,
        params (string WorkCenter, decimal EstimatedMinutes)[] steps)
    {
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest($"PROD-{Guid.NewGuid():N}"[..12], "Prodotto scheduling finita", null));
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var material = await CreateMaterialAsync(materialStock);
        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(material.Code, 1, null)]));

        var routingResponse = await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/routing",
            new ReplaceRoutingRequest(steps
                .Select((step, index) => new RoutingStepRequest($"Fase {index + 1}", null, step.WorkCenter, step.EstimatedMinutes))
                .ToList()));
        return (await routingResponse.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    [Fact]
    public async Task ScheduleTwoOrders_OnSharedWorkCenter_SecondOrderStartsAfterCapacityFilled()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var bottleneckName = $"Linea-{suffix}";
        var otherCenterName = $"Collaudo-{suffix}";

        await CreateWorkCenterAsync(bottleneckName, dailyCapacityMinutes: 60);
        await CreateWorkCenterAsync(otherCenterName, dailyCapacityMinutes: 480);

        var productA = await CreateProductWithRoutingAsync(
            materialStock: 100,
            (bottleneckName, 60),
            (otherCenterName, 15));
        var productB = await CreateProductWithRoutingAsync(
            materialStock: 100,
            (bottleneckName, 60));

        var startFrom = new DateTime(2027, 4, 1, 0, 0, 0, DateTimeKind.Utc);

        var orderAResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(productA.Id, 1, null, null, null, null, null));
        var orderA = (await orderAResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{orderA.Id}/schedule?startFrom={startFrom:O}", null);

        var orderBResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(productB.Id, 1, null, null, null, null, null));
        var orderB = (await orderBResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var scheduleBResponse = await _adminClient.PostAsync(
            $"/api/work-orders/{orderB.Id}/schedule?startFrom={startFrom:O}", null);
        scheduleBResponse.EnsureSuccessStatusCode();
        var scheduledB = (await scheduleBResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var bottleneckStepB = scheduledB.Operations.Single(op => op.WorkCenter == bottleneckName);
        Assert.Equal(startFrom.Date.AddDays(1), bottleneckStepB.PlannedStartAt!.Value.Date);
    }

    [Fact]
    public async Task CapacityPlan_AfterScheduling_ReflectsCommittedMinutesOnBottleneckDay()
    {
        var workCenterName = $"Linea-{Guid.NewGuid():N}"[..16];
        await CreateWorkCenterAsync(workCenterName, dailyCapacityMinutes: 60);
        var product = await CreateProductWithRoutingAsync(materialStock: 100, (workCenterName, 60));
        var startFrom = new DateTime(2027, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/schedule?startFrom={startFrom:O}", null);

        var planResponse = await _adminClient.GetAsync(
            $"/api/work-centers/capacity-plan?from={startFrom:O}&days=3");
        planResponse.EnsureSuccessStatusCode();
        var plan = (await planResponse.Content.ReadFromJsonAsync<List<WorkCenterCapacityPlanRow>>())!;

        var dayRow = plan.Single(row =>
            row.WorkCenterName == workCenterName &&
            row.Day == DateOnly.FromDateTime(startFrom));
        Assert.Equal(60, dayRow.CapacityMinutes);
        Assert.Equal(60, dayRow.CommittedMinutes);
        Assert.Equal(0, dayRow.FreeMinutes);
    }

    [Fact]
    public async Task CapacityPlan_InvalidDays_ReturnsBadRequestInItalian()
    {
        var response = await _adminClient.GetAsync("/api/work-centers/capacity-plan?days=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("giorni", body, StringComparison.OrdinalIgnoreCase);
    }
}
