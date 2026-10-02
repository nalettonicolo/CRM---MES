using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ClosedXML.Excel;
using CrmMes.Api.Controllers;

namespace CrmMes.Api.Tests;

public class SupplierCatalogImportTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    public SupplierCatalogImportTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
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
    public async Task ImportCsv_CreatesSupplierMaterialAndCatalogLink()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var csv = "supplierCode,supplierName,code,name,partNumber,unit,stock,minStock,description,unitPrice,leadTimeDays\n" +
                   $"SUP-{suffix},Fornitore CSV,MAT-{suffix},Materiale CSV,PN-{suffix},pz,10,2,Descrizione,4.5,7\n";
        var bytes = Encoding.UTF8.GetBytes(csv);

        using var content = BuildFileContent(bytes, "catalogo.csv", "text/csv");
        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-csv", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<ImportSummary>();

        Assert.Equal(1, summary!.Imported);
        Assert.Equal(1, summary.CreatedMaterials);
        Assert.Equal(1, summary.CreatedLinks);

        var searchResponse = await _adminClient.GetFromJsonAsync<List<object>>($"/api/supplier-catalog/search?q=PN-{suffix}");
        Assert.Single(searchResponse!);

        var upperCaseSearchResponse = await _adminClient.GetFromJsonAsync<List<CatalogSearchResultResponse>>(
            $"/api/supplier-catalog/search?q={Uri.EscapeDataString($"MATERIALE CSV")}");
        Assert.Contains(upperCaseSearchResponse!, result => result.PartNumber == $"PN-{suffix}");
    }

    [Fact]
    public async Task ImportCsv_MissingRequiredColumns_ReturnsBadRequest()
    {
        var bytes = Encoding.UTF8.GetBytes("code,name\nMAT-1,Materiale\n");
        using var content = BuildFileContent(bytes, "catalogo.csv", "text/csv");

        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-csv", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportExcel_CreatesSupplierMaterialAndCatalogLink()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Catalogo");
        string[] headers = ["supplierCode", "supplierName", "code", "name", "partNumber", "unit", "stock", "minStock", "description", "unitPrice", "leadTimeDays"];
        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        worksheet.Cell(2, 1).Value = $"SUP-{suffix}";
        worksheet.Cell(2, 2).Value = "Fornitore Excel";
        worksheet.Cell(2, 3).Value = $"MAT-{suffix}";
        worksheet.Cell(2, 4).Value = "Materiale Excel";
        worksheet.Cell(2, 5).Value = $"PN-{suffix}";
        worksheet.Cell(2, 6).Value = "pz";
        worksheet.Cell(2, 7).Value = 20;
        worksheet.Cell(2, 8).Value = 5;
        worksheet.Cell(2, 9).Value = "Descrizione Excel";
        worksheet.Cell(2, 10).Value = 12.5;
        worksheet.Cell(2, 11).Value = 14;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        using var content = BuildFileContent(stream.ToArray(), "catalogo.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-excel", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<ImportSummary>();

        Assert.Equal(1, summary!.Imported);
        Assert.Equal(1, summary.CreatedMaterials);
        Assert.Equal(1, summary.CreatedLinks);

        var searchResponse = await _adminClient.GetFromJsonAsync<List<object>>($"/api/supplier-catalog/search?q=PN-{suffix}");
        Assert.Single(searchResponse!);
    }

    [Fact]
    public async Task ImportMetel_CreatesMaterialsFromADelimitedListino()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var csv = "Marca;Codice;Descrizione;UM;Prezzo;EAN\n" +
                  $"ABB;1S-{suffix};Interruttore {suffix};PZ;8,40;8012345678901\n";
        using var content = BuildFileContent(Encoding.UTF8.GetBytes(csv), "listino.txt", "text/plain");
        content.Add(new StringContent($"SUP-{suffix}"), "supplierCode");
        content.Add(new StringContent("Elettroforniture spa"), "supplierName");

        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-metel", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = await response.Content.ReadFromJsonAsync<ImportSummary>();
        Assert.Equal(1, summary!.Imported);
        Assert.Equal(1, summary.CreatedMaterials);

        var found = await _adminClient.GetFromJsonAsync<List<CatalogSearchResultResponse>>(
            $"/api/supplier-catalog/search?q=1S-{suffix}");
        var row = Assert.Single(found!);
        Assert.Equal($"SUP-{suffix}", row.SupplierCode);
        Assert.Equal($"1S-{suffix}", row.PartNumber);
        Assert.Equal(8.40m, row.UnitPrice);
    }

    [Fact]
    public async Task ImportMetel_WhenTheModuleIsOff_IsRejected()
    {
        var previous = await _adminClient.GetFromJsonAsync<CompanyProfileResponse>("/api/company-profile");
        try
        {
            var save = await _adminClient.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
                "Officina Test Metel", null, null, null, null, "mechanical", ["purchasing"]));
            save.EnsureSuccessStatusCode();

            var csv = "Marca;Codice;Descrizione;UM;Prezzo\nABB;X1;Interruttore;PZ;1\n";
            using var content = BuildFileContent(Encoding.UTF8.GetBytes(csv), "listino.txt", "text/plain");
            var response = await _adminClient.PostAsync("/api/supplier-catalog/import-metel", content);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Listini Metel", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            if (previous is not null)
            {
                await _adminClient.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
                    string.IsNullOrWhiteSpace(previous.CompanyName) ? "Azienda test" : previous.CompanyName,
                    previous.VatNumber, previous.Address, previous.Phone, previous.Email, previous.Sector, previous.EnabledModules));
            }
        }
    }

    [Fact]
    public async Task ImportMetel_EmptyFile_ReturnsBadRequest()
    {
        using var content = BuildFileContent(Encoding.UTF8.GetBytes("\n"), "listino.txt", "text/plain");
        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-metel", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ImportExcel_MissingRequiredColumns_ReturnsBadRequest()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Catalogo");
        worksheet.Cell(1, 1).Value = "code";
        worksheet.Cell(1, 2).Value = "name";
        worksheet.Cell(2, 1).Value = "MAT-1";
        worksheet.Cell(2, 2).Value = "Materiale";

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        using var content = BuildFileContent(stream.ToArray(), "catalogo.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-excel", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
