using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
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

    private static MultipartFormDataContent BuildFileContent(byte[] bytes, string fileName, string contentType)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task ImportBillOfMaterial_FromCsv_ReplacesBom()
    {
        var product = await CreateProductAsync();
        var materialA = await CreateMaterialAsync();
        var materialB = await CreateMaterialAsync();
        var csv = "materialCode,quantity,notes\n" +
                   $"{materialA.Code},3,nota A\n" +
                   $"{materialB.Code},1.5,\n";
        using var content = BuildFileContent(Encoding.UTF8.GetBytes(csv), "distinta.csv", "text/csv");

        var response = await _adminClient.PostAsync($"/api/products/{product.Id}/bom/import", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(2, updated!.BillOfMaterial.Count);
        Assert.Contains(updated.BillOfMaterial, item => item.MaterialCode == materialA.Code && item.Quantity == 3 && item.Notes == "nota A");
    }

    [Fact]
    public async Task ImportBillOfMaterial_FromExcel_ReplacesBom()
    {
        var product = await CreateProductAsync();
        var materialA = await CreateMaterialAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Distinta");
        worksheet.Cell(1, 1).Value = "materialCode";
        worksheet.Cell(1, 2).Value = "quantity";
        worksheet.Cell(1, 3).Value = "notes";
        worksheet.Cell(2, 1).Value = materialA.Code;
        worksheet.Cell(2, 2).Value = 4;
        worksheet.Cell(2, 3).Value = "riga excel";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        using var content = BuildFileContent(stream.ToArray(), "distinta.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var response = await _adminClient.PostAsync($"/api/products/{product.Id}/bom/import", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Single(updated!.BillOfMaterial);
        Assert.Equal(materialA.Code, updated.BillOfMaterial[0].MaterialCode);
        Assert.Equal(4, updated.BillOfMaterial[0].Quantity);
    }

    [Fact]
    public async Task ImportBillOfMaterial_WithUnknownMaterialCode_ReturnsBadRequest()
    {
        var product = await CreateProductAsync();
        var csv = "materialCode,quantity\nCODICE-INESISTENTE,1\n";
        using var content = BuildFileContent(Encoding.UTF8.GetBytes(csv), "distinta.csv", "text/csv");

        var response = await _adminClient.PostAsync($"/api/products/{product.Id}/bom/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportBillOfMaterial_EmptyFile_ReturnsBadRequest()
    {
        var product = await CreateProductAsync();
        using var content = BuildFileContent(Encoding.UTF8.GetBytes("materialCode,quantity\n"), "distinta.csv", "text/csv");

        var response = await _adminClient.PostAsync($"/api/products/{product.Id}/bom/import", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
