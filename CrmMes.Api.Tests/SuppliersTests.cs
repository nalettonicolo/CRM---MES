using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class SuppliersTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public SuppliersTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public async Task CreateSupplier_ThenList_ContainsIt()
    {
        var code = $"SUP-{Guid.NewGuid():N}";

        var createResponse = await _adminClient.PostAsJsonAsync(
            "/api/suppliers",
            new CreateSupplierRequest("Fornitore Test", code, null, null));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await _adminClient.GetFromJsonAsync<List<SupplierResponse>>("/api/suppliers");

        Assert.Contains(listResponse!, supplier => supplier.Code == code);
    }

    [Fact]
    public async Task CreateSupplier_DuplicateCode_ReturnsConflict()
    {
        var code = $"DUP-{Guid.NewGuid():N}";
        await _adminClient.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest("Primo", code, null, null));

        var response = await _adminClient.PostAsJsonAsync("/api/suppliers", new CreateSupplierRequest("Secondo", code, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateSupplier_AsOperator_ReturnsForbidden()
    {
        var operatorAuth = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator", "sup-op");
        using var operatorClient = _fixture.Factory.AuthenticatedClient(operatorAuth.Token);

        var response = await operatorClient.PostAsJsonAsync(
            "/api/suppliers",
            new CreateSupplierRequest("Non autorizzato", $"NA-{Guid.NewGuid():N}", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
