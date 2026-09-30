using System.Net;
using System.Net.Http.Json;
using CrmMes.Api.Controllers;
using CrmMes.Api.Services;
using CrmMes.Core.Models;

namespace CrmMes.Api.Tests;

/// <summary>Shared setup for the food and site-work tests: a material in stock with a known lot, a
/// product whose bill of materials uses it, a work order that consumed the lot.</summary>
public abstract class FoodAndSiteTestBase : IClassFixture<AdminSeededApiTestFixture>
{
    protected readonly AdminSeededApiTestFixture Fixture;
    protected readonly HttpClient Admin;

    protected FoodAndSiteTestBase(AdminSeededApiTestFixture fixture)
    {
        Fixture = fixture;
        Admin = fixture.Factory.AuthenticatedClient(fixture.Admin.Token);
    }

    protected static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    protected async Task<MaterialResponse> CreateMaterialAsync(string code, string name, string unit = "kg")
    {
        var response = await Admin.PostAsJsonAsync("/api/materials", new CreateMaterialRequest(code, name, unit, 0, 0));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<MaterialResponse>())!;
    }

    protected async Task<ProductResponse> CreateProductAsync(string code, params (string Material, decimal Quantity)[] bom)
    {
        var product = (await (await Admin.PostAsJsonAsync("/api/products", new CreateProductRequest(code, $"Prodotto {code}", null)))
            .Content.ReadFromJsonAsync<ProductResponse>())!;
        if (bom.Length > 0)
        {
            (await Admin.PutAsJsonAsync($"/api/products/{product.Id}/bom",
                new ReplaceBillOfMaterialRequest(bom.Select(line => new BillOfMaterialItemRequest(line.Material, line.Quantity, null)).ToList())))
                .EnsureSuccessStatusCode();
        }

        return product;
    }

    protected async Task<WorkOrderResponse> CreateWorkOrderAsync(Guid productId, decimal quantity, Guid? customerId = null)
    {
        var area = (await (await Admin.PostAsJsonAsync("/api/areas", new CreateAreaRequest($"Area {Suffix()}", $"A-{Suffix()}")))
            .Content.ReadFromJsonAsync<Area>())!;
        var response = await Admin.PostAsJsonAsync("/api/work-orders",
            new CreateWorkOrderRequest(productId, quantity, null, area.Id, null, null, null, CustomerId: customerId));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WorkOrderResponse>())!;
    }

    protected async Task<CustomerResponse> CreateCustomerAsync(string name)
    {
        var code = $"C-{Suffix()}";
        var response = await Admin.PostAsJsonAsync("/api/customers",
            new SaveCustomerRequest(name, code, "IT12345678901", null, null, "Via Milano 5, Bergamo", null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CustomerResponse>())!;
    }

    protected async Task ConsumeForWorkOrderAsync(Guid workOrderId)
    {
        var generated = (await (await Admin.PostAsync($"/api/work-orders/{workOrderId}/generate-withdrawal-slip", null))
            .Content.ReadFromJsonAsync<WorkOrderWithdrawalSlipResponse>())!;
        (await Admin.PostAsync($"/api/withdrawal-slips/{generated.WithdrawalSlipId}/close", null)).EnsureSuccessStatusCode();
    }

    protected async Task<TransportDocumentResponse> ShipWorkOrderAsync(Guid workOrderId)
    {
        var draft = (await (await Admin.PostAsync($"/api/transport-documents/from-work-order/{workOrderId}", null))
            .Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
        var issued = await Admin.PostAsJsonAsync($"/api/transport-documents/{draft.Id}/issue", new IssueTransportDocumentRequest(null));
        issued.EnsureSuccessStatusCode();
        return (await issued.Content.ReadFromJsonAsync<TransportDocumentResponse>())!;
    }
}

public class LotRecallTests : FoodAndSiteTestBase
{
    public LotRecallTests(AdminSeededApiTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task MaterialLotRecall_FollowsTheLotToWorkOrdersShipmentsAndCustomers()
    {
        var suffix = Suffix();
        var flour = await CreateMaterialAsync($"FAR-{suffix}", "Farina 00");
        var suspect = (await (await Admin.PostAsJsonAsync("/api/material-lots", new CreateMaterialLotRequest(flour.Code, $"SOSP-{suffix}", 50, null)))
            .Content.ReadFromJsonAsync<MaterialLotSummaryResponse>())!;
        var product = await CreateProductAsync($"PANE-{suffix}", (flour.Code, 2));
        var customer = await CreateCustomerAsync($"Panetteria {suffix}");

        var shipped = await CreateWorkOrderAsync(product.Id, 10, customer.Id);   // uses 20 kg
        await ConsumeForWorkOrderAsync(shipped.Id);
        var document = await ShipWorkOrderAsync(shipped.Id);
        var kept = await CreateWorkOrderAsync(product.Id, 5);                     // uses 10 kg, never shipped
        await ConsumeForWorkOrderAsync(kept.Id);

        var recall = (await Admin.GetFromJsonAsync<RecallResponse>($"/api/recall/material-lot/{suspect.Id}"))!;
        Assert.Contains(suspect.LotNumber, recall.Subject);
        Assert.Equal(2, recall.WorkOrders.Count);
        Assert.Equal(20, recall.WorkOrders.Single(o => o.Id == shipped.Id).ConsumedQuantity);
        Assert.Equal(10, recall.WorkOrders.Single(o => o.Id == kept.Id).ConsumedQuantity);
        var shipment = Assert.Single(recall.Shipments);
        Assert.Equal(document.DocumentCode, shipment.DocumentCode);
        Assert.Equal(shipped.ProductLotNumber, shipment.LotNumber);
        Assert.Equal([customer.Name], recall.Customers);
        Assert.Contains(recall.Warnings, w => w.Contains("Ancora in magazzino"));   // 20 kg left in the lot

        // The same chain, starting from the finished product lot.
        var byProduct = (await Admin.GetFromJsonAsync<RecallResponse>($"/api/recall/product-lot?lotNumber={shipped.ProductLotNumber}"))!;
        Assert.Equal(shipped.Id, Assert.Single(byProduct.WorkOrders).Id);
        Assert.Equal([customer.Name], byProduct.Customers);
    }

    [Fact]
    public async Task Recall_UnknownLots()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync($"/api/recall/material-lot/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync("/api/recall/product-lot?lotNumber=NON-ESISTE")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.GetAsync("/api/recall/product-lot?lotNumber=%20")).StatusCode);
    }
}

public class FoodLabelTests : FoodAndSiteTestBase
{
    public FoodLabelTests(AdminSeededApiTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Label_ListsIngredientsByQuantity_WithTheirAllergens_AndTheUseByDate()
    {
        var suffix = Suffix();
        var flour = await CreateMaterialAsync($"FAR-{suffix}", "Farina sacco 25 kg");
        var milk = await CreateMaterialAsync($"LAT-{suffix}", "Latte intero");
        var salt = await CreateMaterialAsync($"SAL-{suffix}", "Sale fino");
        (await Admin.PutAsJsonAsync($"/api/food/materials/{flour.Id}",
            new SaveMaterialFoodInfoRequest("farina di grano tenero tipo 00", ["GLUTEN"]))).EnsureSuccessStatusCode();
        (await Admin.PutAsJsonAsync($"/api/food/materials/{milk.Id}",
            new SaveMaterialFoodInfoRequest("latte intero", ["milk", "milk"]))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PutAsJsonAsync($"/api/food/materials/{salt.Id}", new SaveMaterialFoodInfoRequest(null, ["unicorno"]))).StatusCode);

        var product = await CreateProductAsync($"BRI-{suffix}", (salt.Code, 0.02m), (flour.Code, 1m), (milk.Code, 0.3m));
        (await Admin.PutAsJsonAsync($"/api/food/products/{product.Id}",
            new SaveProductFoodInfoRequest("Brioche al latte", 20, false, "Conservare in luogo fresco e asciutto", "400 g"))).EnsureSuccessStatusCode();
        var order = await CreateWorkOrderAsync(product.Id, 100);

        var label = (await Admin.GetFromJsonAsync<FoodLabelResponse>($"/api/food/work-orders/{order.Id}/label"))!;
        Assert.Equal("Brioche al latte", label.SalesName);
        Assert.Equal(["farina di grano tenero tipo 00", "latte intero", "Sale fino"], label.Ingredients.Select(i => i.Name));
        Assert.Equal(["gluten"], label.Ingredients[0].Allergens);
        Assert.Equal(["Cereali contenenti glutine", "Latte"], label.Allergens);   // Annex II order
        Assert.Equal(label.ProductionDate.AddDays(20), label.ExpiryDate);
        Assert.False(label.UseByDate);
        Assert.Contains(label.Warnings, w => w.Contains(salt.Code));              // salt has no food data yet
        Assert.Equal(order.ProductLotNumber, label.LotNumber);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PutAsJsonAsync($"/api/food/products/{product.Id}", new SaveProductFoodInfoRequest(null, 0, false, null, null))).StatusCode);
    }

    [Fact]
    public void Gs1_CheckDigitAndSsccLayout()
    {
        // Example SSCC of the GS1 General Specifications: (00) 1 0614141 123456789 7.
        Assert.Equal(7, Gs1.CheckDigit("10614141123456789"));
        Assert.True(Gs1.IsValidSscc("106141411234567897"));
        Assert.False(Gs1.IsValidSscc("106141411234567890"));
        // GTIN-13 of a well-known product: 400638133393 -> check digit 1.
        Assert.Equal(1, Gs1.CheckDigit("400638133393"));

        var sscc = Gs1.Sscc("8012345", 42);
        Assert.Equal(18, sscc.Length);
        Assert.StartsWith("08012345", sscc);
        Assert.True(Gs1.IsValidSscc(sscc));
        Assert.Throws<ArgumentException>(() => Gs1.Sscc("123", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Gs1.Sscc("8012345", Gs1.MaxSerial("8012345") + 1));
    }

    [Fact]
    public async Task Sscc_NeedsTheGs1Prefix_ThenHandsOutConsecutiveValidCodes()
    {
        // The profile may already exist from another test class sharing nothing: create it here if not.
        await Admin.PutAsJsonAsync("/api/company-profile", new SaveCompanyProfileRequest(
            "Alimentari Verdi srl", null, null, null, null, "food", null));
        var suffix = Suffix();
        var product = await CreateProductAsync($"PAL-{suffix}");
        var order = await CreateWorkOrderAsync(product.Id, 240);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PutAsJsonAsync("/api/food/gs1-prefix", new SetGs1PrefixRequest("12AB567"))).StatusCode);
        (await Admin.PutAsJsonAsync("/api/food/gs1-prefix", new SetGs1PrefixRequest("8012345"))).EnsureSuccessStatusCode();

        var first = (await (await Admin.PostAsJsonAsync("/api/food/logistic-units", new CreateLogisticUnitRequest(order.Id, null, 120)))
            .Content.ReadFromJsonAsync<LogisticUnitResponse>())!;
        var second = (await (await Admin.PostAsJsonAsync("/api/food/logistic-units", new CreateLogisticUnitRequest(order.Id, null, null)))
            .Content.ReadFromJsonAsync<LogisticUnitResponse>())!;
        Assert.True(Gs1.IsValidSscc(first.Sscc));
        Assert.True(Gs1.IsValidSscc(second.Sscc));
        Assert.Equal(long.Parse(first.Sscc[8..17]) + 1, long.Parse(second.Sscc[8..17]));
        Assert.Equal(120, first.Quantity);
        Assert.Equal(240, second.Quantity);            // whole work order by default
        Assert.Equal(order.ProductLotNumber, first.LotNumber);

        var units = (await Admin.GetFromJsonAsync<List<LogisticUnitResponse>>($"/api/food/logistic-units?workOrderId={order.Id}"))!;
        Assert.Equal(2, units.Count);
    }
}

public class HaccpTests : FoodAndSiteTestBase
{
    public HaccpTests(AdminSeededApiTestFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Readings_OutOfLimitsNeedACorrectiveAction_AndLandInTheRegister()
    {
        var point = (await (await Admin.PostAsJsonAsync("/api/haccp/control-points", new SaveHaccpControlPointRequest(
            $"Cella frigo {Suffix()}", "Laboratorio", "Proliferazione batterica", "°C", 0, 4, "Due volte al giorno",
            "Spostare la merce in altra cella e chiamare il tecnico")))
            .Content.ReadFromJsonAsync<HaccpControlPointResponse>())!;
        Assert.True(point.IsNumeric);

        var ok = await Admin.PostAsJsonAsync($"/api/haccp/control-points/{point.Id}/readings", new AddHaccpReadingRequest(3.2m, null, null, null, null));
        ok.EnsureSuccessStatusCode();
        Assert.True((await ok.Content.ReadFromJsonAsync<HaccpReadingResponse>())!.Compliant);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PostAsJsonAsync($"/api/haccp/control-points/{point.Id}/readings", new AddHaccpReadingRequest(7.5m, null, null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Admin.PostAsJsonAsync($"/api/haccp/control-points/{point.Id}/readings", new AddHaccpReadingRequest(null, true, null, null, null))).StatusCode);

        var bad = await Admin.PostAsJsonAsync($"/api/haccp/control-points/{point.Id}/readings",
            new AddHaccpReadingRequest(7.5m, true, "Merce spostata in cella 2, tecnico chiamato", null, null));
        bad.EnsureSuccessStatusCode();
        Assert.False((await bad.Content.ReadFromJsonAsync<HaccpReadingResponse>())!.Compliant);   // limits decide, not the client

        var today = DateTime.UtcNow.Date;
        var register = (await Admin.GetFromJsonAsync<List<HaccpReadingResponse>>(
            $"/api/haccp/readings?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&controlPointId={point.Id}"))!;
        Assert.Equal(2, register.Count);
        var nonCompliant = (await Admin.GetFromJsonAsync<List<HaccpReadingResponse>>(
            $"/api/haccp/readings?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&controlPointId={point.Id}&nonCompliantOnly=true"))!;
        Assert.Equal(7.5m, Assert.Single(nonCompliant).Value);

        var points = (await Admin.GetFromJsonAsync<List<HaccpControlPointResponse>>("/api/haccp/control-points"))!;
        Assert.False(points.Single(p => p.Id == point.Id).LastCompliant);
    }

    [Fact]
    public async Task YesNoPoints_OperatorsRecord_ButOnlyWarehouseDefinesPoints()
    {
        var point = (await (await Admin.PostAsJsonAsync("/api/haccp/control-points", new SaveHaccpControlPointRequest(
            $"Sanificazione affettatrice {Suffix()}", "Banco", null, null, null, null, "A fine turno", null)))
            .Content.ReadFromJsonAsync<HaccpControlPointResponse>())!;
        Assert.False(point.IsNumeric);

        var auth = await TestAuth.CreateUserWithRoleAsync(Fixture.Factory, Fixture.Admin.Token, "Operator", "haccp-op");
        var operatorClient = Fixture.Factory.AuthenticatedClient(auth.Token);
        (await operatorClient.PostAsJsonAsync($"/api/haccp/control-points/{point.Id}/readings",
            new AddHaccpReadingRequest(null, true, null, "Eseguita", null))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.PostAsJsonAsync("/api/haccp/control-points",
            new SaveHaccpControlPointRequest("Nuovo", null, null, null, null, null, null, null))).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync("/api/haccp/control-points",
            new SaveHaccpControlPointRequest("Limiti invertiti", null, null, "°C", 5, 1, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync($"/api/haccp/control-points/{point.Id}/readings",
            new AddHaccpReadingRequest(null, true, null, null, DateTime.UtcNow.AddDays(1)))).StatusCode);

        (await Admin.PutAsJsonAsync($"/api/haccp/control-points/{point.Id}", new SaveHaccpControlPointRequest(
            point.Name, "Banco", null, null, null, null, "A fine turno", null, IsActive: false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PostAsJsonAsync($"/api/haccp/control-points/{point.Id}/readings",
            new AddHaccpReadingRequest(null, true, null, null, null))).StatusCode);
    }
}

public class SiteReportTests : FoodAndSiteTestBase
{
    // A real 1x1 transparent PNG.
    private const string Signature = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    public SiteReportTests(AdminSeededApiTestFixture fixture) : base(fixture)
    {
    }

    private static string BigSignature()
    {
        // A valid PNG header followed by padding: over 100 bytes, as a drawn signature always is.
        var bytes = new byte[400];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        return "data:image/png;base64," + Convert.ToBase64String(bytes);
    }

    [Fact]
    public async Task Report_DraftThenSigned_BooksHoursAndMaterialsInTheJobCost()
    {
        var suffix = Suffix();
        var cable = await CreateMaterialAsync($"CAV-{suffix}", "Cavo FG16 3x2,5", "m");
        var product = await CreateProductAsync($"IMP-{suffix}");
        var customer = await CreateCustomerAsync($"Condominio {suffix}");
        var order = await CreateWorkOrderAsync(product.Id, 1, customer.Id);

        var open = (await Admin.GetFromJsonAsync<List<SiteWorkOrderResponse>>($"/api/site-reports/open-work-orders?q={suffix}"))!;
        Assert.Equal("Via Milano 5, Bergamo", Assert.Single(open).CustomerAddress);

        var createResponse = await Admin.PostAsJsonAsync("/api/site-reports", new SaveSiteReportRequest(
            order.Id, DateTime.UtcNow.Date, null, "Posa linea montante e quadro di piano", null,
            [new SiteReportHoursRequest("Luca Bassi", null, 240), new SiteReportHoursRequest("Sara Neri", null, 180)],
            [new SiteReportMaterialRequest(cable.Code, null, 35, null), new SiteReportMaterialRequest(null, "Tasselli 8 mm", 20, "pz")]));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var draft = (await createResponse.Content.ReadFromJsonAsync<SiteReportResponse>())!;
        Assert.StartsWith($"RI-{DateTime.UtcNow.Year}-", draft.Code);
        Assert.Equal("Via Milano 5, Bergamo", draft.SiteAddress);             // from the customer
        Assert.Equal("m", draft.Materials.Single(m => m.MaterialCode == cable.Code).Unit);

        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync($"/api/site-reports/{draft.Id}/sign",
            new SignSiteReportRequest("Mario Rossi", "data:image/png;base64,AAAA"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync($"/api/site-reports/{draft.Id}/sign",
            new SignSiteReportRequest(" ", BigSignature()))).StatusCode);

        var signed = await Admin.PostAsJsonAsync($"/api/site-reports/{draft.Id}/sign", new SignSiteReportRequest("Mario Rossi", BigSignature()));
        signed.EnsureSuccessStatusCode();
        var report = (await signed.Content.ReadFromJsonAsync<SiteReportResponse>())!;
        Assert.Equal("Signed", report.Status);
        Assert.NotNull(report.SignedAt);

        // Frozen.
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PutAsJsonAsync($"/api/site-reports/{draft.Id}", new SaveSiteReportRequest(
            order.Id, null, null, "modifica", null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync($"/api/site-reports/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.PostAsJsonAsync($"/api/site-reports/{draft.Id}/sign",
            new SignSiteReportRequest("Mario Rossi", BigSignature()))).StatusCode);

        // Hours became labor entries; materials count in the job's actual cost.
        var costing = (await Admin.GetFromJsonAsync<WorkOrderCostingResult>($"/api/work-orders/{order.Id}/costing"))!;
        Assert.Equal(420, costing.Labor.Where(l => l.Kind == "Ore registrate").Sum(l => l.Minutes));
        Assert.Contains(costing.Materials, m => m.MaterialCode == cable.Code && m.Quantity == 35);
        Assert.Contains(costing.Materials, m => m.MaterialCode == "Tasselli 8 mm" && m.UnpricedQuantity == 20);
        Assert.DoesNotContain(costing.Warnings, w => w.Contains("Nessuna distinta di prelievo"));
    }

    [Fact]
    public async Task Report_Validation_AndDraftEditing()
    {
        var product = await CreateProductAsync($"IMP-{Suffix()}");
        var order = await CreateWorkOrderAsync(product.Id, 1);

        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync("/api/site-reports",
            new SaveSiteReportRequest(null, null, null, "x", null, null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync("/api/site-reports", new SaveSiteReportRequest(
            order.Id, null, null, "x", null, [new SiteReportHoursRequest("Tecnico", null, 0)], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync("/api/site-reports", new SaveSiteReportRequest(
            order.Id, null, null, "x", null, null, [new SiteReportMaterialRequest("NON-ESISTE", null, 1, null)]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync("/api/site-reports", new SaveSiteReportRequest(
            order.Id, DateTime.UtcNow.AddDays(5), null, "x", null, null, null))).StatusCode);

        // An operator (technician) writes and edits drafts; a signature needs a description.
        var auth = await TestAuth.CreateUserWithRoleAsync(Fixture.Factory, Fixture.Admin.Token, "Operator", "cantiere-op");
        var technician = Fixture.Factory.AuthenticatedClient(auth.Token);
        var draft = (await (await technician.PostAsJsonAsync("/api/site-reports", new SaveSiteReportRequest(
            order.Id, null, "Via Roma 1", "", null, [new SiteReportHoursRequest("Luca", null, 60)], null)))
            .Content.ReadFromJsonAsync<SiteReportResponse>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await technician.PostAsJsonAsync($"/api/site-reports/{draft.Id}/sign",
            new SignSiteReportRequest("Cliente", BigSignature()))).StatusCode);

        var edited = await technician.PutAsJsonAsync($"/api/site-reports/{draft.Id}", new SaveSiteReportRequest(
            order.Id, null, "Via Roma 1", "Sostituito interruttore differenziale", null,
            [new SiteReportHoursRequest("Luca", null, 90), new SiteReportHoursRequest("Anna", null, 30)], null));
        edited.EnsureSuccessStatusCode();
        Assert.Equal(120, (await edited.Content.ReadFromJsonAsync<SiteReportResponse>())!.Hours.Sum(h => h.Minutes));

        var drafts = (await technician.GetFromJsonAsync<List<SiteReportSummaryResponse>>($"/api/site-reports?workOrderId={order.Id}&status=Draft"))!;
        Assert.Equal(120, Assert.Single(drafts).TotalMinutes);
        Assert.Equal(HttpStatusCode.NoContent, (await technician.DeleteAsync($"/api/site-reports/{draft.Id}")).StatusCode);
    }

    [Fact]
    public void SignatureValidation_AcceptsOnlyRealPngDataUrls()
    {
        Assert.False(SiteReportsController.IsValidSignature(null));
        Assert.False(SiteReportsController.IsValidSignature("data:image/jpeg;base64,/9j/4AAQ"));
        Assert.False(SiteReportsController.IsValidSignature("data:image/png;base64,non-base64!!"));
        Assert.False(SiteReportsController.IsValidSignature(Signature));   // real PNG but too small to be a signature
        Assert.True(SiteReportsController.IsValidSignature(BigSignature()));
        Assert.False(SiteReportsController.IsValidSignature("data:image/png;base64," + Convert.ToBase64String(new byte[400_000])));
    }
}

public class TechnicianWebAppTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly AdminSeededApiTestFixture _fixture;

    public TechnicianWebAppTests(AdminSeededApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Page_IsServedWithSecurityHeaders_AndItsScriptsFromTheSameOrigin()
    {
        var client = _fixture.Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var redirect = await client.GetAsync("/tecnici");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/tecnici/", redirect.Headers.Location!.OriginalString);

        var page = await client.GetAsync("/tecnici/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("data-app=\"tecnici\"", html);
        Assert.DoesNotContain("<script>", html);              // no inline script: the CSP would block it
        var csp = page.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Equal("nosniff", page.Headers.GetValues("X-Content-Type-Options").Single());

        foreach (var asset in new[] { "/tecnici/app.js", "/tecnici/app.css" })
        {
            var response = await client.GetAsync(asset);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var script = await client.GetStringAsync("/tecnici/app.js");
        Assert.DoesNotContain("innerHTML", script);             // data is rendered with textContent only
        Assert.Contains("/api/site-reports", script);

        // The API itself does not get the page's headers.
        var health = await client.GetAsync("/health");
        Assert.False(health.Headers.Contains("Content-Security-Policy"));
    }
}
