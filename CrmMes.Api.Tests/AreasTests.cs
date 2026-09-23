using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

public class AreasTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public AreasTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<Area> CreateAreaAsync()
    {
        var code = $"AREA-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {code}", code));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Area>())!;
    }

    [Fact]
    public async Task AssignUser_ThenGetDetail_IncludesUser()
    {
        var area = await CreateAreaAsync();
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "area-user");

        var assignResponse = await _adminClient.PostAsync($"/api/areas/{area.Id}/users/{operatorAuth.UserId}", null);
        Assert.Equal(HttpStatusCode.NoContent, assignResponse.StatusCode);

        var detailResponse = await _adminClient.GetAsync($"/api/areas/{area.Id}/detail");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<AreaDetailResponse>();

        Assert.Single(detail!.Users, user => user.Id == operatorAuth.UserId);
    }

    [Fact]
    public async Task UnassignUser_RemovesFromDetail()
    {
        var area = await CreateAreaAsync();
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "area-unassign");
        await _adminClient.PostAsync($"/api/areas/{area.Id}/users/{operatorAuth.UserId}", null);

        var unassignResponse = await _adminClient.DeleteAsync($"/api/areas/{area.Id}/users/{operatorAuth.UserId}");
        Assert.Equal(HttpStatusCode.NoContent, unassignResponse.StatusCode);

        var detailResponse = await _adminClient.GetAsync($"/api/areas/{area.Id}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<AreaDetailResponse>();

        Assert.DoesNotContain(detail!.Users, user => user.Id == operatorAuth.UserId);
    }

    [Fact]
    public async Task GetDetail_IncludesWorkOrdersAssignedToTheArea()
    {
        var area = await CreateAreaAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var productResponse = await _adminClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"PROD-{suffix}", "Prodotto test", null));
        productResponse.EnsureSuccessStatusCode();
        var product = (await productResponse.Content.ReadFromJsonAsync<ProductResponse>())!;

        var workOrderResponse = await _adminClient.PostAsJsonAsync(
            "/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, area.Id, null, null, null));
        workOrderResponse.EnsureSuccessStatusCode();
        var workOrder = (await workOrderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var detailResponse = await _adminClient.GetAsync($"/api/areas/{area.Id}/detail");
        var detail = await detailResponse.Content.ReadFromJsonAsync<AreaDetailResponse>();

        Assert.Single(detail!.WorkOrders, wo => wo.Id == workOrder.Id);
    }
}
