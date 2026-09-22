using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class WorkCentersTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public WorkCentersTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<WorkCenterResponse> CreateWorkCenterAsync(decimal dailyCapacityMinutes = 480)
    {
        var code = $"WC-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/work-centers",
            new CreateWorkCenterRequest(code, $"Centro {code}", "Descrizione", dailyCapacityMinutes));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkCenterResponse>())!;
    }

    [Fact]
    public async Task CreateWorkCenter_ThenList_ReturnsIt()
    {
        var workCenter = await CreateWorkCenterAsync();

        var listResponse = await _adminClient.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers");

        Assert.Contains(listResponse!, w => w.Id == workCenter.Id && w.Code == workCenter.Code);
    }

    [Fact]
    public async Task CreateWorkCenter_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP-{Guid.NewGuid():N}"[..12];
        await _adminClient.PostAsJsonAsync("/api/work-centers", new CreateWorkCenterRequest(code, "Primo", null, 480));

        var response = await _adminClient.PostAsJsonAsync("/api/work-centers", new CreateWorkCenterRequest(code, "Secondo", null, 480));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateWorkCenter_AsOperator_ReturnsForbidden()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "wc-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync(
            "/api/work-centers", new CreateWorkCenterRequest($"OP-{Guid.NewGuid():N}", "Non autorizzato", null, 480));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task EditWorkCenter_UpdatesNameAndCapacity()
    {
        var workCenter = await CreateWorkCenterAsync();

        var response = await _adminClient.PutAsJsonAsync(
            $"/api/work-centers/{workCenter.Id}",
            new EditWorkCenterRequest("Rinominato", "Nuova descrizione", 600));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<WorkCenterResponse>();
        Assert.Equal("Rinominato", updated!.Name);
        Assert.Equal(600, updated.DailyCapacityMinutes);
    }

    [Fact]
    public async Task DeactivateWorkCenter_RemovesItFromActiveList()
    {
        var workCenter = await CreateWorkCenterAsync();

        var deleteResponse = await _adminClient.DeleteAsync($"/api/work-centers/{workCenter.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await _adminClient.GetFromJsonAsync<List<WorkCenterResponse>>("/api/work-centers");
        Assert.DoesNotContain(listResponse!, w => w.Id == workCenter.Id);
    }

    [Fact]
    public async Task LoadOverview_WithOpenOperations_SumsMinutesAndComputesBacklogDays()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var workCenter = await CreateWorkCenterAsync(dailyCapacityMinutes: 100);

        var materialResponse = await _adminClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest($"MAT-{suffix}", "Materiale test", "pz", 1000, 0));
        materialResponse.EnsureSuccessStatusCode();
        var material = (await materialResponse.Content.ReadFromJsonAsync<MaterialResponse>())!;

        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{suffix}", "Prodotto test", null));
        productResponse.EnsureSuccessStatusCode();
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(material.Code, 1, null)]));

        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/routing",
            new ReplaceRoutingRequest(
            [
                new RoutingStepRequest("Fase 1", null, workCenter.Name, 150)
            ]));

        var workOrderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders",
            new { productId = product.Id, quantity = 1 });
        workOrderResponse.EnsureSuccessStatusCode();
        var workOrderId = (await workOrderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!.Id;

        await _adminClient.PostAsync($"/api/work-orders/{workOrderId}/release?force=true", null);

        var loadResponse = await _adminClient.GetFromJsonAsync<List<WorkCenterLoadResponse>>("/api/work-centers/load");

        var line = Assert.Single(loadResponse!, l => l.Id == workCenter.Id);
        Assert.Equal(150, line.PendingMinutes);
        Assert.Equal(1, line.OpenOperations);
        Assert.Equal(1.5m, line.BacklogDays);
    }
}
