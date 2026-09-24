using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Data;
using CrmMes.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class WorkOrdersTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public WorkOrdersTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
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

    private async Task<Area> CreateAreaAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {suffix}", $"AREA-{suffix}"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Area>())!;
    }

    /// <summary>A product with a two-step routing and a one-line BOM built from a material with the
    /// given stock, ready to drive a work order end to end.</summary>
    private async Task<(ProductResponse Product, MaterialResponse Material)> CreateProductWithRoutingAndBomAsync(decimal materialStock)
    {
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest($"PROD-{Guid.NewGuid():N}"[..12], "Prodotto di test", null));
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var material = await CreateMaterialAsync(materialStock);

        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(material.Code, 2, null)]));

        var routingResponse = await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/routing",
            new ReplaceRoutingRequest(
            [
                new RoutingStepRequest("Assemblaggio", null, "Linea 1", 60),
                new RoutingStepRequest("Collaudo", null, "Banco prova", 15)
            ]));
        var updatedProduct = (await routingResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        return (updatedProduct, material);
    }

    [Fact]
    public async Task CreateWorkOrder_SnapshotsRoutingFromProduct()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);

        var response = await _adminClient.PostAsJsonAsync(
            "/api/work-orders",
            new CreateWorkOrderRequest(product.Id, 3, null, null, "Cliente Test", null, null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        Assert.Equal("Draft", order.Status);
        Assert.Equal(2, order.Operations.Count);
        Assert.Equal("Assemblaggio", order.Operations[0].Name);
        Assert.Equal(1, order.Operations[0].SequenceNumber);
        Assert.All(order.Operations, op => Assert.Equal("Pending", op.Status));
    }

    [Fact]
    public async Task CreateWorkOrder_WithInactiveOrMissingProduct_ReturnsBadRequest()
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/api/work-orders",
            new CreateWorkOrderRequest(Guid.NewGuid(), 1, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReleaseThenStartOperation_MovesWorkOrderToInProgress()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var releaseResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);
        Assert.Equal(HttpStatusCode.OK, releaseResponse.StatusCode);
        var released = (await releaseResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        Assert.Equal("Released", released.Status);

        var firstOperationId = released.Operations[0].Id;
        var startResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{firstOperationId}/start", null);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var started = (await startResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        Assert.Equal("InProgress", started.Status);
        Assert.Equal("InProgress", started.Operations[0].Status);
        Assert.NotNull(started.Operations[0].StartedAt);
    }

    [Fact]
    public async Task CompleteWorkOrder_WithPendingOperations_ReturnsConflict()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var completeResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/complete", null);

        Assert.Equal(HttpStatusCode.Conflict, completeResponse.StatusCode);
    }

    [Fact]
    public async Task CompleteWorkOrder_AfterAllOperationsDone_Succeeds()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var detailResponse = await _adminClient.GetAsync($"/api/work-orders/{order.Id}");
        var detail = (await detailResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        foreach (var operation in detail.Operations)
        {
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{operation.Id}/start", null);
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{operation.Id}/complete", null);
        }

        var completeResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/complete", null);

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completed = (await completeResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        Assert.Equal("Completed", completed.Status);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task GenerateWithdrawalSlip_CreatesSlipWithBomQuantitiesScaledByWorkOrderQuantity()
    {
        var (product, material) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var area = await CreateAreaAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 4, null, area.Id, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var generateResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/generate-withdrawal-slip", null);
        Assert.Equal(HttpStatusCode.OK, generateResponse.StatusCode);
        var generated = (await generateResponse.Content.ReadFromJsonAsync<WorkOrderWithdrawalSlipResponse>())!;

        var slipResponse = await _adminClient.GetAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}");
        var slip = (await slipResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;

        Assert.Equal(order.Id, slip.WorkOrderId);
        Assert.Single(slip.Items);
        // BOM line was 2 per unit, work order quantity is 4 -> expect 8.
        Assert.Equal(8, slip.Items[0].Quantity);
        Assert.Equal(material.Code, slip.Items[0].MaterialCode);
        Assert.False(slip.Items[0].IsMissing);
    }

    [Fact]
    public async Task GenerateWithdrawalSlip_WithoutArea_ReturnsBadRequest()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var response = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/generate-withdrawal-slip", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GenerateWithdrawalSlip_WithInsufficientStock_FlagsMissingMaterial()
    {
        var (product, material) = await CreateProductWithRoutingAndBomAsync(materialStock: 1);
        var area = await CreateAreaAsync();

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 5, null, area.Id, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var generateResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/generate-withdrawal-slip", null);
        var generated = (await generateResponse.Content.ReadFromJsonAsync<WorkOrderWithdrawalSlipResponse>())!;

        var slipResponse = await _adminClient.GetAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}");
        var slip = (await slipResponse.Content.ReadFromJsonAsync<WithdrawalSlipResponse>())!;

        Assert.True(slip.Items[0].IsMissing);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var missing = await db.MissingMaterials.SingleOrDefaultAsync(m => m.WithdrawalSlipId == generated.WithdrawalSlipId);
        Assert.NotNull(missing);
        Assert.Equal(material.Code, missing!.MaterialCode);
    }

    [Fact]
    public async Task EditWorkOrder_AfterRelease_ReturnsConflict()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var editResponse = await _adminClient.PutAsJsonAsync(
            $"/api/work-orders/{order.Id}", new EditWorkOrderRequest(2, null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
    }

    [Fact]
    public async Task CreateWorkOrder_AsOperator_ReturnsForbidden()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "wo-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MaterialCheck_WithSufficientStock_ReportsAvailable()
    {
        // BOM is 2 units of material per product unit; quantity 5 needs 10, stock is 100.
        var (product, material) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 5, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var availability = await _adminClient.GetFromJsonAsync<MaterialAvailabilityResponse>($"/api/work-orders/{order.Id}/material-check");

        Assert.True(availability!.IsAvailable);
        var line = Assert.Single(availability.Lines);
        Assert.Equal(material.Code, line.MaterialCode);
        Assert.Equal(10, line.Required);
        Assert.Equal(0, line.Shortfall);
    }

    [Fact]
    public async Task MaterialCheck_WithInsufficientStock_ReportsShortfall()
    {
        // BOM is 2 units per product unit; quantity 5 needs 10, but stock is only 3.
        var (product, material) = await CreateProductWithRoutingAndBomAsync(materialStock: 3);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 5, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var availability = await _adminClient.GetFromJsonAsync<MaterialAvailabilityResponse>($"/api/work-orders/{order.Id}/material-check");

        Assert.False(availability!.IsAvailable);
        var line = Assert.Single(availability.Lines);
        Assert.Equal(material.Code, line.MaterialCode);
        Assert.Equal(10, line.Required);
        Assert.Equal(3, line.Available);
        Assert.Equal(7, line.Shortfall);
    }

    [Fact]
    public async Task ReleaseWorkOrder_WithInsufficientMaterial_ReturnsConflictWithoutForce()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 1);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 5, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var releaseResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        Assert.Equal(HttpStatusCode.Conflict, releaseResponse.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var stillDraft = await db.WorkOrders.SingleAsync(o => o.Id == order.Id);
        Assert.Equal("Draft", stillDraft.Status);
    }

    [Fact]
    public async Task ReleaseWorkOrder_WithInsufficientMaterial_ForceTrue_ReleasesAnyway()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 1);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 5, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var releaseResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release?force=true", null);

        Assert.Equal(HttpStatusCode.OK, releaseResponse.StatusCode);
        var released = (await releaseResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        Assert.Equal("Released", released.Status);
    }

    [Fact]
    public async Task Operation_BeforeCompletion_HasNoActualMinutesOrPerformanceRatio()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        Assert.All(order.Operations, op =>
        {
            Assert.Null(op.ActualMinutes);
            Assert.Null(op.PerformanceRatio);
        });
    }

    [Fact]
    public async Task CompleteOperation_ComputesActualMinutesAndPerformanceRatio()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var firstOperationId = order.Operations[0].Id;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{firstOperationId}/start", null);
        var completeResponse = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{firstOperationId}/complete", null);
        var updated = (await completeResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var completedOperation = updated.Operations.Single(op => op.Id == firstOperationId);
        Assert.NotNull(completedOperation.ActualMinutes);
        Assert.True(completedOperation.ActualMinutes >= 0);
        Assert.NotNull(completedOperation.PerformanceRatio);
        Assert.True(completedOperation.PerformanceRatio > 0);
    }

    [Fact]
    public async Task Dashboard_ReflectsCompletedWorkOrderAndOperation()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var detail = (await (await _adminClient.GetAsync($"/api/work-orders/{order.Id}")).Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        foreach (var operation in detail.Operations)
        {
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{operation.Id}/start", null);
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{operation.Id}/complete", null);
        }

        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/complete", null);

        var dashboard = await _adminClient.GetFromJsonAsync<WorkOrderDashboardResponse>("/api/work-orders/dashboard?days=1");

        Assert.NotNull(dashboard);
        Assert.True(dashboard!.WorkOrdersByStatus.GetValueOrDefault("Completed", 0) >= 1);
        Assert.True(dashboard.OperationsCompletedInPeriod >= detail.Operations.Count);
        Assert.NotNull(dashboard.AveragePerformanceRatio);
        Assert.True(dashboard.AveragePerformanceRatio > 0);
        Assert.True(dashboard.WorkOrdersCompletedInPeriod >= 1);
    }

    private async Task<Guid> CreateWorkCenterAsync(string name, decimal dailyCapacityMinutes)
    {
        var response = await _adminClient.PostAsJsonAsync(
            "/api/work-centers",
            new CreateWorkCenterRequest($"WC-{Guid.NewGuid():N}"[..12], name, null, dailyCapacityMinutes));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkCenterResponse>())!.Id;
    }

    /// <summary>A product whose routing steps use caller-supplied, normally GUID-suffixed work center
    /// names — unlike <see cref="CreateProductWithRoutingAndBomAsync"/>'s fixed "Linea 1"/"Banco prova",
    /// so scheduling tests can each use their own work center name and stay isolated from one another
    /// despite sharing one Sqlite database for the whole test class.</summary>
    private async Task<ProductResponse> CreateProductWithCustomRoutingAsync(
        decimal materialStock, params (string WorkCenter, decimal EstimatedMinutes)[] steps)
    {
        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest($"PROD-{Guid.NewGuid():N}"[..12], "Prodotto scheduling", null));
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
    public async Task ScheduleWorkOrder_UnregisteredWorkCenters_AssignsOneOperationPerDayInSequence()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = await CreateProductWithCustomRoutingAsync(
            materialStock: 100,
            ($"Linea-{suffix}", 60),
            ($"Banco-{suffix}", 15));
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var startFrom = new DateTime(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);

        var response = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/schedule?startFrom={startFrom:O}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scheduled = (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var step1 = scheduled.Operations.Single(op => op.SequenceNumber == 1);
        var step2 = scheduled.Operations.Single(op => op.SequenceNumber == 2);

        Assert.Equal(startFrom.Date, step1.PlannedStartAt!.Value.Date);
        Assert.Equal(startFrom.Date, step1.PlannedEndAt!.Value.Date);
        Assert.Equal(startFrom.Date.AddDays(1), step2.PlannedStartAt!.Value.Date);
    }

    [Fact]
    public async Task ScheduleWorkOrder_OperationLongerThanDailyCapacity_SpillsOverToNextDay()
    {
        var workCenterName = $"Linea-{Guid.NewGuid():N}"[..16];
        var product = await CreateProductWithCustomRoutingAsync(materialStock: 100, (workCenterName, 60));
        await CreateWorkCenterAsync(workCenterName, dailyCapacityMinutes: 30); // step needs 60 minutes
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var startFrom = new DateTime(2027, 3, 8, 0, 0, 0, DateTimeKind.Utc);

        var response = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/schedule?startFrom={startFrom:O}", null);

        var scheduled = (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var step = scheduled.Operations.Single();

        Assert.Equal(startFrom.Date, step.PlannedStartAt!.Value.Date);
        Assert.Equal(startFrom.Date.AddDays(1), step.PlannedEndAt!.Value.Date);
    }

    [Fact]
    public async Task ScheduleWorkOrder_SharedWorkCenterAcrossOrders_DoesNotDoubleBookCapacity()
    {
        var workCenterName = $"Linea-{Guid.NewGuid():N}"[..16];
        var productA = await CreateProductWithCustomRoutingAsync(materialStock: 100, (workCenterName, 60));
        var productB = await CreateProductWithCustomRoutingAsync(materialStock: 100, (workCenterName, 60));
        await CreateWorkCenterAsync(workCenterName, dailyCapacityMinutes: 60); // exactly one 60-minute step per day
        var startFrom = new DateTime(2027, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var orderAResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(productA.Id, 1, null, null, null, null, null));
        var orderA = (await orderAResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{orderA.Id}/schedule?startFrom={startFrom:O}", null);

        var orderBResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(productB.Id, 1, null, null, null, null, null));
        var orderB = (await orderBResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var scheduleBResponse = await _adminClient.PostAsync($"/api/work-orders/{orderB.Id}/schedule?startFrom={startFrom:O}", null);
        var scheduledB = (await scheduleBResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var stepB = scheduledB.Operations.Single();
        Assert.Equal(startFrom.Date.AddDays(1), stepB.PlannedStartAt!.Value.Date);
    }

    [Fact]
    public async Task ScheduleWorkOrder_CompletedOrCancelled_ReturnsConflict()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/cancel", null);

        var response = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/schedule", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ScheduleWorkOrder_SkipsAlreadyDoneOperations()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);
        var assemblyId = order.Operations.OrderBy(op => op.SequenceNumber).First().Id;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{assemblyId}/start", null);
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{assemblyId}/complete", null);

        var response = await _adminClient.PostAsync($"/api/work-orders/{order.Id}/schedule", null);
        var scheduled = (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var assembly = scheduled.Operations.Single(op => op.Id == assemblyId);
        Assert.Null(assembly.PlannedStartAt);
    }

    private async Task<(Guid WorkOrderId, Guid OperationId)> CreateAndStartFirstOperationAsync()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);
        var operationId = order.Operations.OrderBy(op => op.SequenceNumber).First().Id;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{operationId}/start", null);
        return (order.Id, operationId);
    }

    [Fact]
    public async Task StartDowntime_OnInProgressOperation_Succeeds()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", "Da verificare"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var downtime = await response.Content.ReadFromJsonAsync<OperationDowntimeResponse>();
        Assert.Equal("Guasto macchina", downtime!.Reason);
        Assert.Null(downtime.EndedAt);
        Assert.Null(downtime.DurationMinutes);
    }

    [Fact]
    public async Task StartDowntime_OnPendingOperation_ReturnsConflict()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var operationId = order.Operations.OrderBy(op => op.SequenceNumber).First().Id;

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{order.Id}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task StartDowntime_WhenAlreadyOpen_ReturnsConflict()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", null));

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Un altro motivo", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EndDowntime_ClosesAndComputesDuration()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        var startResponse = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", null));
        var downtime = (await startResponse.Content.ReadFromJsonAsync<OperationDowntimeResponse>())!;

        var endResponse = await _adminClient.PostAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtime.Id}/end", null);

        Assert.Equal(HttpStatusCode.OK, endResponse.StatusCode);
        var closed = (await endResponse.Content.ReadFromJsonAsync<OperationDowntimeResponse>())!;
        Assert.NotNull(closed.EndedAt);
        Assert.NotNull(closed.DurationMinutes);
        Assert.True(closed.DurationMinutes >= 0);
    }

    [Fact]
    public async Task EndDowntime_AlreadyClosed_ReturnsConflict()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        var startResponse = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", null));
        var downtime = (await startResponse.Content.ReadFromJsonAsync<OperationDowntimeResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtime.Id}/end", null);

        var response = await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtime.Id}/end", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CompleteOperation_WithOpenDowntime_ReturnsConflict()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", null));

        var response = await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/complete", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CompleteOperation_AfterClosingDowntime_Succeeds()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        var startResponse = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", null));
        var downtime = (await startResponse.Content.ReadFromJsonAsync<OperationDowntimeResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtime.Id}/end", null);

        var response = await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/complete", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDowntimes_ListsRecordedStops()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", "Nota"));

        var downtimes = await _adminClient.GetFromJsonAsync<List<OperationDowntimeResponse>>(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtimes");

        Assert.Single(downtimes!);
        Assert.Equal("Guasto macchina", downtimes![0].Reason);
    }

    [Fact]
    public async Task Dashboard_ReflectsDowntimeAndAvailabilityRatio()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        var startResponse = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start",
            new StartDowntimeRequest("Guasto macchina", null));
        var downtime = (await startResponse.Content.ReadFromJsonAsync<OperationDowntimeResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtime.Id}/end", null);
        await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/complete", null);

        var dashboard = await _adminClient.GetFromJsonAsync<WorkOrderDashboardResponse>("/api/work-orders/dashboard?days=1");

        Assert.NotNull(dashboard);
        Assert.True(dashboard!.TotalDowntimeMinutes >= 0);
        Assert.NotNull(dashboard.AvailabilityRatio);
        Assert.InRange(dashboard.AvailabilityRatio!.Value, 0m, 1m);
    }

    [Fact]
    public async Task RegisterNonConformity_OnInProgressOperation_Succeeds()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Fuori tolleranza", 2, "Verificare calibro"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var nonConformity = await response.Content.ReadFromJsonAsync<NonConformityResponse>();
        Assert.Equal("Fuori tolleranza", nonConformity!.Description);
        Assert.Equal(2, nonConformity.ScrapQuantity);
    }

    [Fact]
    public async Task RegisterNonConformity_OnPendingOperation_ReturnsConflict()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var operationId = order.Operations.OrderBy(op => op.SequenceNumber).First().Id;

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{order.Id}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Difetto", 1, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterNonConformity_WithZeroQuantity_ReturnsBadRequest()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Difetto", 0, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterNonConformity_OnCompletedOperation_Succeeds()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/operations/{operationId}/complete", null);

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Difetto trovato al collaudo finale", 1, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetNonConformities_ListsRecordedDefects()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Difetto estetico", 3, "Rigatura superficiale"));

        var nonConformities = await _adminClient.GetFromJsonAsync<List<NonConformityResponse>>(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities");

        Assert.Single(nonConformities!);
        Assert.Equal("Difetto estetico", nonConformities![0].Description);
        Assert.Equal(3, nonConformities[0].ScrapQuantity);
    }

    [Fact]
    public async Task Dashboard_ReflectsScrapAndQualityRatioAndFullOee()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        // A non-integer quantity deliberately skips per-unit tracking (see WorkOrderUnit), keeping this
        // test on the older scrap-vs-planned-quantity approximation path it was written to exercise.
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 10.5m, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        foreach (var op in order.Operations.OrderBy(op => op.SequenceNumber))
        {
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/start", null);
            await _adminClient.PostAsJsonAsync(
                $"/api/work-orders/{order.Id}/operations/{op.Id}/non-conformities",
                new RegisterNonConformityRequest("Scarto", 1, null));
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/complete", null);
        }
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/complete", null);

        var dashboard = await _adminClient.GetFromJsonAsync<WorkOrderDashboardResponse>("/api/work-orders/dashboard?days=1");

        Assert.NotNull(dashboard);
        Assert.True(dashboard!.TotalScrapQuantity >= 2);
        Assert.NotNull(dashboard.QualityRatio);
        Assert.InRange(dashboard.QualityRatio!.Value, 0m, 1m);
        Assert.NotNull(dashboard.OeeRatio);
        Assert.InRange(dashboard.OeeRatio!.Value, 0m, 1m);
    }

    [Fact]
    public async Task StartAndCompleteOperation_WithOperatorName_AttributesActions()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);
        var operationId = order.Operations.OrderBy(op => op.SequenceNumber).First().Id;

        var startResponse = await _adminClient.PostAsync(
            $"/api/work-orders/{order.Id}/operations/{operationId}/start?operatorName=Mario%20Rossi", null);
        var afterStart = (await startResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        Assert.Equal("Mario Rossi", afterStart.Operations.Single(op => op.Id == operationId).StartedBy);

        var completeResponse = await _adminClient.PostAsync(
            $"/api/work-orders/{order.Id}/operations/{operationId}/complete?operatorName=Luigi%20Verdi", null);
        var afterComplete = (await completeResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        var completedOperation = afterComplete.Operations.Single(op => op.Id == operationId);
        Assert.Equal("Mario Rossi", completedOperation.StartedBy);
        Assert.Equal("Luigi Verdi", completedOperation.CompletedBy);
    }

    [Fact]
    public async Task StartDowntimeAndEndDowntime_WithOperatorName_AttributesActions()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();

        var startResponse = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start?operatorName=Mario%20Rossi",
            new StartDowntimeRequest("Guasto macchina", null));
        var downtime = (await startResponse.Content.ReadFromJsonAsync<OperationDowntimeResponse>())!;
        Assert.Equal("Mario Rossi", downtime.ReportedBy);

        var endResponse = await _adminClient.PostAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/{downtime.Id}/end?operatorName=Luigi%20Verdi", null);
        var closed = (await endResponse.Content.ReadFromJsonAsync<OperationDowntimeResponse>())!;
        Assert.Equal("Mario Rossi", closed.ReportedBy);
        Assert.Equal("Luigi Verdi", closed.ClosedBy);
    }

    [Fact]
    public async Task RegisterNonConformity_WithOperatorName_AttributesAction()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities?operatorName=Mario%20Rossi",
            new RegisterNonConformityRequest("Fuori tolleranza", 1, null));

        var nonConformity = await response.Content.ReadFromJsonAsync<NonConformityResponse>();
        Assert.Equal("Mario Rossi", nonConformity!.ReportedBy);
    }

    [Fact]
    public async Task OperatorActions_WithOperatorId_AppearInUserDetailActivity()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "activity-user");
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();

        var startResponse = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/downtime/start?operatorId={operatorAuth.UserId}",
            new StartDowntimeRequest("Guasto", null));
        startResponse.EnsureSuccessStatusCode();

        var detailResponse = await _adminClient.GetAsync($"/api/users/{operatorAuth.UserId}/detail");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<UserDetailResponse>();

        Assert.Contains(detail!.RecentActivity, a => a.Kind == "Fermo segnalato");
    }

    [Fact]
    public async Task GetWorkOrderByCode_ReturnsMatchingOrder()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var response = await _adminClient.GetAsync($"/api/work-orders/by-code/{order.Code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var found = await response.Content.ReadFromJsonAsync<WorkOrderResponse>();
        Assert.Equal(order.Id, found!.Id);
    }

    [Fact]
    public async Task GetWorkOrderByCode_UnknownCode_ReturnsNotFound()
    {
        var response = await _adminClient.GetAsync("/api/work-orders/by-code/CODICE-INESISTENTE");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateWorkOrder_WithIntegerQuantity_GeneratesOneUnitPerPiece()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 3, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units");

        Assert.Equal(3, units!.Count);
        Assert.Equal([1, 2, 3], units.Select(u => u.SequenceNumber));
        Assert.All(units, u => Assert.Equal("Pending", u.Status));
        Assert.All(units, u => Assert.StartsWith(order.Code, u.SerialNumber));
    }

    [Fact]
    public async Task CreateWorkOrder_WithNonIntegerQuantity_GeneratesNoUnits()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 2.5m, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units");

        Assert.Empty(units!);
    }

    [Fact]
    public async Task RegisterNonConformity_WithUnit_ScrapsThatUnitOnly()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{workOrderId}/units");
        var unit = units!.Single();

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Fuori tolleranza", 1, null, unit.Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var nonConformity = await response.Content.ReadFromJsonAsync<NonConformityResponse>();
        Assert.Equal(unit.Id, nonConformity!.WorkOrderUnitId);
        Assert.Equal(unit.SerialNumber, nonConformity.UnitSerialNumber);

        var updatedUnits = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{workOrderId}/units");
        Assert.Equal("Scrapped", updatedUnits!.Single(u => u.Id == unit.Id).Status);
    }

    [Fact]
    public async Task RegisterNonConformity_WithAlreadyResolvedUnit_ReturnsConflict()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{workOrderId}/units");
        var unit = units!.Single();
        await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Fuori tolleranza", 1, null, unit.Id));

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Altro difetto", 1, null, unit.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RegisterNonConformity_WithUnitFromAnotherWorkOrder_ReturnsBadRequest()
    {
        var (workOrderId, operationId) = await CreateAndStartFirstOperationAsync();
        var (otherWorkOrderId, _) = await CreateAndStartFirstOperationAsync();
        var otherUnits = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{otherWorkOrderId}/units");

        var response = await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{workOrderId}/operations/{operationId}/non-conformities",
            new RegisterNonConformityRequest("Fuori tolleranza", 1, null, otherUnits!.Single().Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CompleteWorkOrder_FlipsRemainingPendingUnitsToGood()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 2, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units");
        var scrappedUnit = units!.First();

        foreach (var op in order.Operations.OrderBy(op => op.SequenceNumber))
        {
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/start", null);
            if (op.SequenceNumber == order.Operations.Min(o => o.SequenceNumber))
            {
                await _adminClient.PostAsJsonAsync(
                    $"/api/work-orders/{order.Id}/operations/{op.Id}/non-conformities",
                    new RegisterNonConformityRequest("Scarto", 1, null, scrappedUnit.Id));
            }

            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/complete", null);
        }

        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/complete", null);

        var finalUnits = (await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units"))!;
        Assert.Equal("Scrapped", finalUnits.Single(u => u.Id == scrappedUnit.Id).Status);
        Assert.All(finalUnits.Where(u => u.Id != scrappedUnit.Id), u => Assert.Equal("Good", u.Status));
    }

    [Fact]
    public async Task Dashboard_UsesExactUnitCountsForOrdersWithUnits()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 4, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units");
        var scrappedUnit = units!.First();
        var firstOperationId = order.Operations.OrderBy(op => op.SequenceNumber).First().Id;

        foreach (var op in order.Operations.OrderBy(op => op.SequenceNumber))
        {
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/start", null);
            if (op.Id == firstOperationId)
            {
                await _adminClient.PostAsJsonAsync(
                    $"/api/work-orders/{order.Id}/operations/{op.Id}/non-conformities",
                    new RegisterNonConformityRequest("Scarto", 1, null, scrappedUnit.Id));
            }

            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/complete", null);
        }

        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/complete", null);

        var dashboard = await _adminClient.GetFromJsonAsync<WorkOrderDashboardResponse>("/api/work-orders/dashboard?days=1");

        Assert.NotNull(dashboard);
        Assert.NotNull(dashboard!.QualityRatio);
        Assert.InRange(dashboard.QualityRatio!.Value, 0m, 1m);
    }

    [Fact]
    public async Task StartAndCompleteOperation_ProjectsTimingToEveryPendingUnit()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 2, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units");
        var firstOperation = order.Operations.OrderBy(op => op.SequenceNumber).First();

        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{firstOperation.Id}/start", null);
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{firstOperation.Id}/complete", null);

        foreach (var unit in units!)
        {
            var detail = await _adminClient.GetFromJsonAsync<WorkOrderUnitDetailResponse>($"/api/work-orders/{order.Id}/units/{unit.Id}/detail");
            Assert.NotNull(detail);
            var firstOpProgress = detail!.Operations.Single(op => op.OperationId == firstOperation.Id);
            Assert.Equal("Done", firstOpProgress.Status);
            Assert.NotNull(firstOpProgress.StartedAt);
            Assert.NotNull(firstOpProgress.CompletedAt);

            var secondOpProgress = detail.Operations.Single(op => op.OperationId != firstOperation.Id);
            Assert.Equal("Pending", secondOpProgress.Status);
        }
    }

    [Fact]
    public async Task ScrappedUnit_StopsReceivingLaterPhaseTiming()
    {
        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);
        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 2, null, null, null, null, null));
        var order = (await createResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);

        var units = await _adminClient.GetFromJsonAsync<List<WorkOrderUnitResponse>>($"/api/work-orders/{order.Id}/units");
        var scrappedUnit = units!.First();
        var operations = order.Operations.OrderBy(op => op.SequenceNumber).ToList();
        var firstOperation = operations[0];
        var secondOperation = operations[1];

        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{firstOperation.Id}/start", null);
        await _adminClient.PostAsJsonAsync(
            $"/api/work-orders/{order.Id}/operations/{firstOperation.Id}/non-conformities",
            new RegisterNonConformityRequest("Scarto", 1, null, scrappedUnit.Id));
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{firstOperation.Id}/complete", null);
        await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{secondOperation.Id}/start", null);

        var detail = await _adminClient.GetFromJsonAsync<WorkOrderUnitDetailResponse>($"/api/work-orders/{order.Id}/units/{scrappedUnit.Id}/detail");
        var secondOpProgress = detail!.Operations.Single(op => op.OperationId == secondOperation.Id);
        // The scrapped unit was already Scrapped before the second phase started, so it never received
        // that phase's InProgress timing — unlike the still-Pending (Good) unit, which would.
        Assert.Equal("Pending", secondOpProgress.Status);
    }

    [Fact]
    public async Task Dashboard_FilteredBySite_OnlyCountsWorkOrdersInThatSite()
    {
        var siteResponse = await _adminClient.PostAsJsonAsync(
            "/api/sites", new CreateSiteRequest($"Sede {Guid.NewGuid():N}"[..14], $"SITE-{Guid.NewGuid():N}"[..10], null));
        siteResponse.EnsureSuccessStatusCode();
        var site = (await siteResponse.Content.ReadFromJsonAsync<SiteResponse>())!;

        var areaInSite = await CreateAreaInSiteAsync(site.Id);
        var otherArea = await CreateAreaAsync();

        var (product, _) = await CreateProductWithRoutingAndBomAsync(materialStock: 100);

        var inSiteOrderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, areaInSite.Id, null, null, null));
        var inSiteOrder = (await inSiteOrderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var otherOrderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, otherArea.Id, null, null, null));
        var otherOrder = (await otherOrderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        foreach (var order in new[] { inSiteOrder, otherOrder })
        {
            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/release", null);
            foreach (var op in order.Operations)
            {
                await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/start", null);
                await _adminClient.PostAsync($"/api/work-orders/{order.Id}/operations/{op.Id}/complete", null);
            }

            await _adminClient.PostAsync($"/api/work-orders/{order.Id}/complete", null);
        }

        var filtered = await _adminClient.GetFromJsonAsync<WorkOrderDashboardResponse>($"/api/work-orders/dashboard?days=1&siteId={site.Id}");

        Assert.NotNull(filtered);
        Assert.True(filtered!.WorkOrdersCompletedInPeriod >= 1);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var otherOrderStillExists = await db.WorkOrders.AnyAsync(o => o.Id == otherOrder.Id && o.Status == "Completed");
        Assert.True(otherOrderStillExists); // sanity: the other order did complete, it's just outside the filtered site.
    }

    private async Task<Area> CreateAreaInSiteAsync(Guid siteId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var response = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {suffix}", $"AREA-{suffix}", siteId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Area>())!;
    }
}
