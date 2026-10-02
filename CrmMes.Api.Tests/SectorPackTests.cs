using System.Net;
using System.Net.Http.Json;
using System.Text;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Api.Tests;

public class SectorPackTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _admin;
    private readonly AdminSeededApiTestFixture _fixture;

    public SectorPackTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
        _admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    [Fact]
    public async Task List_AllEightPacks_AreAvailable()
    {
        var packs = await _admin.GetFromJsonAsync<List<SectorPackSummaryResponse>>("/api/sector-packs");
        Assert.NotNull(packs);
        Assert.Equal(8, packs.Count);
        Assert.All(packs, p => Assert.True(p.Available, $"Il pacchetto {p.Key} dovrebbe essere disponibile."));
    }

    [Fact]
    public async Task EplanPreview_ParsesTwoDataLines()
    {
        const string csv = """
            PartNumber;Quantity;Description
            REL-01;2;Relè interfaccia
            FUS-10;5;Fusibile 10A
            """;

        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await _admin.PostAsync("/api/sector-packs/eplan-bom/preview", content);
        response.EnsureSuccessStatusCode();

        var preview = await response.Content.ReadFromJsonAsync<SectorPackPreviewResponse<EplanBomRowDto>>();
        Assert.NotNull(preview);
        Assert.Equal(2, preview.RowCount);
        Assert.Equal(0, preview.ErrorCount);
    }

    [Fact]
    public async Task EplanImport_CreatesMaterialsAndReplacesBom()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = await CreateProductAsync(suffix);
        var csv = $"""
            PartNumber;Quantity;Description
            NEW-{suffix};2;Componente nuovo
            """;

        using var content = new StringContent(csv, Encoding.UTF8, "text/plain");
        var response = await _admin.PostAsync($"/api/sector-packs/eplan-bom/import?productId={product.Id}", content);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SectorPackImportResponse>();
        Assert.NotNull(result);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.MaterialsCreated);

        var bom = await _admin.GetFromJsonAsync<ProductResponse>($"/api/products/{product.Id}");
        Assert.NotNull(bom);
        Assert.Single(bom.BillOfMaterial);
        Assert.Equal($"NEW-{suffix}", bom.BillOfMaterial[0].MaterialCode);
    }

    [Fact]
    public async Task WireImport_StoresTechnicalDocument()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = await CreateProductAsync(suffix);
        const string csv = """
            From;To;Section;Color
            Q1:1;M1:2;1,5;GNYE
            """;

        using var content = new StringContent(csv, Encoding.UTF8, "text/plain");
        (await _admin.PostAsync($"/api/sector-packs/wire-list/import?productId={product.Id}", content)).EnsureSuccessStatusCode();

        var docs = await _admin.GetFromJsonAsync<List<TechnicalDocumentResponse>>(
            $"/api/engineering/products/{product.Id}/documents");
        Assert.NotNull(docs);
        Assert.Contains(docs, d => d.Title == "Lista cavi" && d.IsCurrent);
    }

    [Fact]
    public async Task SalApply_CreatesProgressCertificate()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var product = await CreateProductAsync(suffix);
        var orderResponse = await _admin.PostAsJsonAsync("/api/work-orders", new CreateWorkOrderRequest(product.Id, 1, null, null, null, null, null));
        orderResponse.EnsureSuccessStatusCode();
        var order = (await orderResponse.Content.ReadFromJsonAsync<WorkOrderResponse>())!;

        var csv = $"""
            WorkOrderCode;PercentComplete;Amount;Notes
            {order.Code};40;1500;Primo SAL
            """;
        using var content = new StringContent(csv, Encoding.UTF8, "text/plain");
        (await _admin.PostAsync("/api/sector-packs/sal/apply", content)).EnsureSuccessStatusCode();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cert = await db.ProgressCertificates.SingleAsync(c => c.WorkOrderId == order.Id);
        Assert.Equal(40, cert.PercentComplete);
        Assert.Equal(1500, cert.Amount);
    }

    [Fact]
    public async Task Cert31Apply_UpdatesMaterialLot()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var code = $"MAT-{suffix}";
        (await _admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, "Materiale cert", "pz", 0, 0))).EnsureSuccessStatusCode();
        const string lotNumber = "LOT-CERT-01";
        (await _admin.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(code, lotNumber, 5, null))).EnsureSuccessStatusCode();

        var csv = $"""
            LotNumber;MaterialCode;CertificateNumber;Issuer;IssuedOn
            {lotNumber};{code};CERT-99;Ente collaudo;2026-01-15
            """;
        using var content = new StringContent(csv, Encoding.UTF8, "text/plain");
        (await _admin.PostAsync("/api/sector-packs/cert-31/apply", content)).EnsureSuccessStatusCode();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var lot = await db.MaterialLots.SingleAsync(l => l.LotNumber == lotNumber && l.MaterialCode == code);
        Assert.Equal("CERT-99", lot.CertificateNumber);
        Assert.Equal("Ente collaudo", lot.CertificateIssuer);
        Assert.NotNull(lot.CertificateIssuedOn);
    }

    [Fact]
    public async Task ScalesReading_PersistsRow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var code = $"MAT-{suffix}";
        (await _admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, "Materiale bilancia", "kg", 0, 0))).EnsureSuccessStatusCode();

        var response = await _admin.PostAsJsonAsync(
            "/api/sector-packs/scales/reading",
            new ScaleReadingRequest(code, 12.5m, "kg", DateTime.UtcNow, "Test"));
        response.EnsureSuccessStatusCode();

        await using var scope = _fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.ScaleReadings.CountAsync(r => r.MaterialCode == code));
    }

    [Fact]
    public async Task Dm3708Generate_ReturnsPlainTextFile()
    {
        var response = await _admin.PostAsJsonAsync(
            "/api/sector-packs/dm3708/generate",
            new Dm3708DeclarationInfo("IMP-1", "Via Roma 1", "Cliente Srl", 6m, null));
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType?.Split(';')[0]);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("DM 37/08", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dm3708SalScalesPreviews_ParseSampleRows()
    {
        using var dm = new StringContent("PlantCode;Address;ClientName;PowerKw;Notes\nP1;Via;Cliente;6;Ok", Encoding.UTF8, "text/plain");
        var dmPreview = await (await _admin.PostAsync("/api/sector-packs/dm3708/preview", dm)).Content.ReadFromJsonAsync<SectorPackPreviewResponse<Dm3708Row>>();
        Assert.Equal(1, dmPreview!.RowCount);

        using var sal = new StringContent("WorkOrderCode;PercentComplete;Amount;Notes\nWO-1;10;100;Ok", Encoding.UTF8, "text/plain");
        var salPreview = await (await _admin.PostAsync("/api/sector-packs/sal/preview", sal)).Content.ReadFromJsonAsync<SectorPackPreviewResponse<SalRow>>();
        Assert.Equal(1, salPreview!.RowCount);

        using var scales = new StringContent($"MaterialCode;WeightKg;Unit;TimestampIso;Notes\nMAT-1;1;kg;{DateTime.UtcNow:O};Ok", Encoding.UTF8, "text/plain");
        var scalePreview = await (await _admin.PostAsync("/api/sector-packs/scales/preview", scales)).Content.ReadFromJsonAsync<SectorPackPreviewResponse<ScaleReadingRow>>();
        Assert.Equal(1, scalePreview!.RowCount);
    }

    [Fact]
    public async Task UnknownKey_ReturnsNotFound()
    {
        var response = await _admin.GetAsync("/api/sector-packs/pacchetto-inesistente");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void ParseCrewCalendar_AndCert31_ValidateColumns()
    {
        var crew = SectorPacks.ParseCrewCalendar("Date;CrewName;SiteOrWorkOrder;Hours\n2026-03-01;Squadra A;Cantiere X;8");
        Assert.Single(crew.Rows);
        Assert.Equal("Squadra A", crew.Rows[0].CrewName);

        var cert = SectorPacks.ParseCert31("LotNumber;MaterialCode;CertificateNumber;Issuer;IssuedOn\nL1;M1;C1;Issuer;2026-02-01");
        Assert.Single(cert.Rows);
        Assert.Equal("C1", cert.Rows[0].CertificateNumber);
    }

    private async Task<ProductResponse> CreateProductAsync(string suffix)
    {
        var response = await _admin.PostAsJsonAsync("/api/products", new CreateProductRequest($"PRD-{suffix}", $"Prodotto {suffix}", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private sealed record EplanBomRowDto(int Row, string PartNumber, decimal Quantity, string Description);
}
