using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class ProductsTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public ProductsTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private async Task<MaterialResponse> CreateMaterialAsync()
    {
        var code = $"MAT-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/materials",
            new CreateMaterialRequest(code, $"Materiale {code}", "pz", 100, 0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>())!;
    }

    private async Task<ProductResponse> CreateProductAsync()
    {
        var code = $"PROD-{Guid.NewGuid():N}"[..12];
        var response = await _adminClient.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest(code, $"Prodotto {code}", "Descrizione di test"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    [Fact]
    public async Task CreateProduct_ThenGet_ReturnsIt()
    {
        var product = await CreateProductAsync();

        var getResponse = await _adminClient.GetAsync($"/api/products/{product.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(product.Code, fetched!.Code);
        Assert.Empty(fetched.BillOfMaterial);
        Assert.Empty(fetched.RoutingSteps);
    }

    [Fact]
    public async Task CreateProduct_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP-{Guid.NewGuid():N}"[..12];
        await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest(code, "Primo", null));

        var response = await _adminClient.PostAsJsonAsync("/api/products", new CreateProductRequest(code, "Secondo", null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_AsOperator_ReturnsForbidden()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "prod-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync(
            "/api/products", new CreateProductRequest($"OP-{Guid.NewGuid():N}", "Non autorizzato", null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceBillOfMaterial_WithUnknownMaterialCode_ReturnsBadRequest()
    {
        var product = await CreateProductAsync();

        var response = await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest("CODICE-INESISTENTE", 1, null)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReplaceBillOfMaterial_WithKnownMaterials_Succeeds()
    {
        var product = await CreateProductAsync();
        var materialA = await CreateMaterialAsync();
        var materialB = await CreateMaterialAsync();

        var response = await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest(
            [
                new BillOfMaterialItemRequest(materialA.Code, 3, "nota"),
                new BillOfMaterialItemRequest(materialB.Code, 1.5m, null)
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(2, updated!.BillOfMaterial.Count);
        Assert.Contains(updated.BillOfMaterial, item => item.MaterialCode == materialA.Code && item.Quantity == 3);
    }

    [Fact]
    public async Task ReplaceBillOfMaterial_CalledTwice_ReplacesRatherThanAccumulates()
    {
        var product = await CreateProductAsync();
        var materialA = await CreateMaterialAsync();
        var materialB = await CreateMaterialAsync();

        await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(materialA.Code, 1, null)]));

        var response = await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/bom",
            new ReplaceBillOfMaterialRequest([new BillOfMaterialItemRequest(materialB.Code, 2, null)]));

        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Single(updated!.BillOfMaterial);
        Assert.Equal(materialB.Code, updated.BillOfMaterial[0].MaterialCode);
    }

    [Fact]
    public async Task ReplaceRouting_AssignsSequentialNumbersInRequestOrder()
    {
        var product = await CreateProductAsync();

        var response = await _adminClient.PutAsJsonAsync(
            $"/api/products/{product.Id}/routing",
            new ReplaceRoutingRequest(
            [
                new RoutingStepRequest("Taglio", null, "Reparto taglio", 30),
                new RoutingStepRequest("Assemblaggio", "Fase di montaggio", "Linea 1", 90),
                new RoutingStepRequest("Collaudo", null, "Banco prova", 20)
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(3, updated!.RoutingSteps.Count);
        Assert.Equal([1, 2, 3], updated.RoutingSteps.Select(step => step.SequenceNumber));
        Assert.Equal("Collaudo", updated.RoutingSteps[2].Name);
    }
}
