using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;

namespace CrmMes.Api.Tests;

/// <summary>Article lists from the company's own software: a title row, then Articolo / Descrizione / CodIVA /
/// UMBase / PrezzoBase / DataCreazione, as in the lists exported by common Italian ERPs.</summary>
public class ArticleImportTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;
    private readonly HttpClient _admin;

    public ArticleImportTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    private static byte[] ListLikeTheCustomers(string prefix, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Foglio1");
        sheet.Cell(1, 1).Value = "LISTA CODICI \"PROVA\"";
        string[] headers = ["Articolo", "Descrizione", "CodIVA", "UMBase", "PrezzoBase", "DataCreazione"];
        for (var c = 0; c < headers.Length; c++) sheet.Cell(2, c + 1).Value = headers[c];
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                var value = rows[r][c];
                var cell = sheet.Cell(r + 3, c + 1);
                switch (value)
                {
                    case null: break;
                    case string text: cell.Value = text.Replace("{p}", prefix); break;
                    case int number: cell.Value = number; break;
                    case double number: cell.Value = number; break;
                    case DateTime date: cell.Value = date; break;
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private async Task<ArticleImportResponse> UploadAsync(byte[] file, bool preview, bool update = false, string name = "lista.xlsx")
    {
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        content.Add(part, "file", name);
        var response = await _admin.PostAsync($"/api/materials/import?preview={preview.ToString().ToLowerInvariant()}&update={update.ToString().ToLowerInvariant()}", content);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ArticleImportResponse>())!;
    }

    [Theory]
    [InlineData("NR", "pz")]
    [InlineData("METRO", "m")]
    [InlineData("CONFEZ", "conf")]
    [InlineData("MATASSA", "matassa")]
    [InlineData("CHILOMET", "km")]
    [InlineData("", "pz")]
    [InlineData("Scatola", "scatola")]
    public void Units_AreTranslated(string source, string expected) => Assert.Equal(expected, ArticleImport.NormalizeUnit(source));

    [Fact]
    public void Parse_FindsTheHeaderUnderTheTitle_AndReportsEveryProblemByRow()
    {
        var rows = new List<string?[]>
        {
            new[] { "LISTA CODICI", null, null },
            new[] { "Descrizione", "Articolo", "PrezzoBase" },       // any column order
            new[] { "RELE FINDER 24V", "WEL0012", "12" },
            new[] { "CAVO", "WEL0100", "1.234,50" },
            new[] { "SOLO DESCRIZIONE", null, null },
            new[] { null, "WEL0101", "abc" },
            new[] { "DOPPIONE", "wel0012", "3" },
            new string?[] { null, null, null },
        };

        var result = ArticleImport.Parse(rows);

        Assert.Equal(["WEL0012", "WEL0100", "WEL0101"], result.Articles.Select(a => a.Code));
        Assert.Equal(1234.50m, result.Articles[1].Price);
        Assert.Equal("WEL0101", result.Articles[2].Name);               // no description: the code
        Assert.Contains(result.Errors, e => e.Row == 5 && e.Message.Contains("Codice mancante"));
        Assert.Contains(result.Errors, e => e.Row == 7 && e.Message.Contains("riga 3"));
        Assert.Contains(result.Warnings, w => w.Row == 6 && w.Message.Contains("non numerico"));
        Assert.Equal("Articolo", result.Columns["code"]);
    }

    [Fact]
    public void Parse_WithoutRecognisableHeaders_SaysWhatIsMissing()
    {
        var result = ArticleImport.Parse([new[] { "colonna1", "colonna2" }, new[] { "a", "b" }]);

        Assert.Empty(result.Articles);
        Assert.Contains("Intestazioni non trovate", result.Errors.Single().Message);
    }

    [Fact]
    public async Task Preview_ChecksWithoutSaving_ThenImportCreatesTheArticles()
    {
        var p = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var file = ListLikeTheCustomers(p,
            ["{p}0001", "INTERRUTTORE DI PRECISIONE MY-COM F30/80 BAUMER", 22, "NR", 205, new DateTime(2010, 7, 26)],
            ["{p}0002", "CAVO FG16 3X2,5", 22, "METRO", 0.85, new DateTime(2021, 12, 6)],
            ["{p}0003", "MATASSA CAVO", null, "", 0, null],
            [null, "RIGA SENZA CODICE", 22, "NR", 1, null]);

        var preview = await UploadAsync(file, preview: true);
        Assert.True(preview.Preview);
        Assert.Equal(3, preview.Created);
        Assert.Equal(1, preview.ErrorCount);
        var before = await _admin.GetFromJsonAsync<List<MaterialResponse>>($"/api/materials?q={p}");
        Assert.Empty(before!);

        var done = await UploadAsync(file, preview: false);
        Assert.Equal(3, done.Created);
        var materials = await _admin.GetFromJsonAsync<List<MaterialResponse>>($"/api/materials?q={p}");
        Assert.Equal(3, materials!.Count);
        var cable = materials.Single(m => m.Code == $"{p}0002");
        Assert.Equal("m", cable.Unit);
        Assert.Equal(0m, cable.Stock);

        var again = await UploadAsync(file, preview: false);
        Assert.Equal(0, again.Created);
        Assert.Equal(3, again.Skipped);
    }

    [Fact]
    public async Task Update_RefreshesChangedArticles_AndNeverTouchesStock()
    {
        var p = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        await UploadAsync(ListLikeTheCustomers(p, ["{p}0001", "RELE", 22, "NR", 10, null], ["{p}0002", "FUSIBILE", 22, "NR", 1, null]), preview: false);
        await _admin.PostAsJsonAsync("/api/material-lots", new { materialCode = $"{p}0001", lotNumber = $"L-{p}", quantity = 40 });

        var result = await UploadAsync(ListLikeTheCustomers(p, ["{p}0001", "RELE FINDER 24V", 22, "NR", 12, null], ["{p}0002", "FUSIBILE", 22, "NR", 1, null]),
            preview: false, update: true);

        Assert.Equal(1, result.Updated);
        Assert.Equal(1, result.Unchanged);
        var rele = (await _admin.GetFromJsonAsync<List<MaterialResponse>>($"/api/materials?q={p}0001"))!.Single();
        Assert.Equal("RELE FINDER 24V", rele.Name);
        Assert.Equal(40m, rele.Stock);
    }

    [Fact]
    public async Task Export_UsesTheSameColumns_SoTheFileGoesBackAndForth()
    {
        var p = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        await UploadAsync(ListLikeTheCustomers(p, ["{p}0001", "SENSORE REED", 22, "NR", 18.5, new DateTime(2010, 7, 26)]), preview: false);

        var response = await _admin.GetAsync("/api/materials/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var rows = ArticleImport.ReadExcel(new MemoryStream(bytes));
        Assert.Equal(["Articolo", "Descrizione", "CodIVA", "UMBase", "PrezzoBase", "DataCreazione"], rows[0].Take(6));
        var parsed = ArticleImport.Parse(rows);
        var article = parsed.Articles.Single(a => a.Code == $"{p}0001");
        Assert.Equal(18.5m, article.Price);
        Assert.Equal(22m, article.VatRate);
        Assert.Equal(new DateTime(2010, 7, 26), article.CreatedAt!.Value.Date);
    }

    [Fact]
    public async Task Import_IsForWarehouseAndPurchasing_AndRejectsOtherFormats()
    {
        var operatorUser = await TestAuth.CreateUserWithRoleAsync(_fixture.Factory, _fixture.Admin.Token, "Operator");
        using var content = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "lista.xlsx" } };
        var denied = await _fixture.Factory.AuthenticatedClient(operatorUser.Token).PostAsync("/api/materials/import?preview=true", content);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var pdf = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "lista.pdf" } };
        var wrong = await _admin.PostAsync("/api/materials/import?preview=true", pdf);
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("Formato non supportato", await wrong.Content.ReadAsStringAsync());
    }
}
