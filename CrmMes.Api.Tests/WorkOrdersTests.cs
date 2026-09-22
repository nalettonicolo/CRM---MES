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
}
