using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class AuthorizationTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public AuthorizationTests(AdminSeededApiTestFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMaterials_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymousClient = _fixture.Factory.CreateClient();

        var response = await anonymousClient.GetAsync("/api/materials");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_AsOperator_ReturnsForbidden()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest($"OP-{Guid.NewGuid():N}", "Materiale operatore", "pz", 0, 0));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateMaterial_AsWarehouse_Succeeds()
    {
        var warehouseAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Warehouse");
        using var warehouseClient = _fixture.Factory.AuthenticatedClient(warehouseAuth.Token);

        var response = await warehouseClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest($"WH-{Guid.NewGuid():N}", "Materiale magazzino", "pz", 10, 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task DeactivateMaterial_AsOperator_ReturnsForbidden()
    {
        using var adminClient = _fixture.Factory.AuthenticatedClient(_fixture.Admin.Token);
        var createResponse = await adminClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest($"DEL-{Guid.NewGuid():N}", "Materiale da disattivare", "pz", 0, 0));
        var material = await createResponse.Content.ReadFromJsonAsync<MaterialResponse>();

        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "del-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.DeleteAsync($"/api/materials/{material!.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreatePurchaseOrder_AsWarehouse_ReturnsForbidden()
    {
        var warehouseAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Warehouse", "po-wh");
        using var warehouseClient = _fixture.Factory.AuthenticatedClient(warehouseAuth.Token);

        var response = await warehouseClient.PostAsJsonAsync(
            "/api/procurement/purchase-orders",
            new CreatePurchaseOrderRequest(Guid.NewGuid(), null, []));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
