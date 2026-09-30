using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CrmMes.Api.Tests;

/// <summary>Exercises the best-effort PDF catalog importer against a synthetic PDF built with QuestPDF
/// (a real, valid PDF — not a hand-rolled byte stub), since no real supplier catalog sample was available
/// to test against. This proves the text-table heuristic works for a well-behaved simple table; it says
/// nothing about how it will fare against an actual Schneider/Pizzato catalog's layout.</summary>
public class SupplierCatalogPdfImportTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _adminClient;

    static SupplierCatalogPdfImportTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public SupplierCatalogPdfImportTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _adminClient = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    /// <summary>A simple catalog table. Landscape and a small font on purpose: with five narrow columns an
    /// A4 portrait page made some random codes wrap inside their cell (hex digits have different widths),
    /// splitting the row in two and making the tests fail at random. The importer handles single-line
    /// rows only, as documented; wrapping belongs to "complex layouts".</summary>
    private static byte[] BuildCatalogPdf(string[] headers, string[][] rows)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontSize(9).DisableFontFeature(FontFeatures.StandardLigatures));

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in headers)
                        {
                            columns.RelativeColumn();
                        }
                    });

                    foreach (var header in headers)
                    {
                        table.Cell().Padding(6).Text(header).Bold();
                    }

                    foreach (var row in rows)
                    {
                        foreach (var cell in row)
                        {
                            table.Cell().Padding(6).Text(cell);
                        }
                    }
                });
            });
        }).GeneratePdf();
    }

    private static MultipartFormDataContent BuildFileContent(byte[] bytes, string fileName)
    {
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        content.Add(fileContent, "file", fileName);
        return content;
    }

    [Fact]
    public async Task ImportPdf_SimpleTextTable_CreatesSupplierMaterialAndCatalogLink()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var pdfBytes = BuildCatalogPdf(
            ["supplierCode", "supplierName", "code", "name", "partNumber"],
            [[$"SUP-{suffix}", "Fornitore PDF", $"MAT-{suffix}", "Materiale PDF", $"PN-{suffix}"]]);

        using var content = BuildFileContent(pdfBytes, "catalogo.pdf");
        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-pdf", content);

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        var summary = await response.Content.ReadFromJsonAsync<ImportSummary>();

        Assert.Equal(1, summary!.Imported);
        Assert.Equal(1, summary.CreatedMaterials);
        Assert.Equal(1, summary.CreatedLinks);

        var searchResponse = await _adminClient.GetFromJsonAsync<List<object>>($"/api/supplier-catalog/search?q=PN-{suffix}");
        Assert.Single(searchResponse!);
    }

    /// <summary>Regression: the PDF draws "ff"/"fi"/"fl" as ligature glyphs; a code containing them must be
    /// imported with plain letters, or searching for it finds nothing (this made the test above fail
    /// whenever its random suffix happened to contain "ff").</summary>
    [Fact]
    public async Task ImportPdf_CodesWithLigatureLetters_AreImportedAsTyped()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var partNumber = $"PN-ff{suffix}fi";
        var pdfBytes = BuildCatalogPdf(
            ["supplierCode", "supplierName", "code", "name", "partNumber"],
            [[$"SUP-L{suffix}", "Fornitore legature", $"MAT-ffl{suffix}", "Staffa fissaggio", partNumber]]);

        using var content = BuildFileContent(pdfBytes, "catalogo-legature.pdf");
        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-pdf", content);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var found = await _adminClient.GetFromJsonAsync<List<object>>($"/api/supplier-catalog/search?q={partNumber}");
        Assert.Single(found!);
    }

    [Fact]
    public async Task ImportPdf_NoRecognizableHeaderRow_ReturnsBadRequestWithClearMessage()
    {
        var pdfBytes = BuildCatalogPdf(
            ["Codice articolo", "Descrizione"],
            [["MAT-1", "Un materiale qualsiasi"]]);

        using var content = BuildFileContent(pdfBytes, "catalogo-non-riconosciuto.pdf");
        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-pdf", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("intestazione", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ImportPdf_EmptyFile_ReturnsBadRequest()
    {
        using var content = BuildFileContent([], "vuoto.pdf");

        var response = await _adminClient.PostAsync("/api/supplier-catalog/import-pdf", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
