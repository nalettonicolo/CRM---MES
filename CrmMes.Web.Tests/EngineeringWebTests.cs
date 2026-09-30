using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class EngineeringWebTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _productId = Guid.NewGuid();

    public EngineeringWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    private EngineeringChange Change(string status) => new(
        Guid.NewGuid(), 7, _productId, "MAC-01", "Pressa idraulica", status == "applied" ? "B" : "A", "Nuova morsettiera", null, "Richiesta cliente", status,
        "A", status == "applied" ? "B" : null, "Ufficio tecnico", DateTime.UtcNow, null, null, null, null, null,
        [new BomLine("MORS-10", 2, null)], [new RoutingLine("Montaggio", null, null, 60)],
        [new BomLine("MORS-10", 4, null), new BomLine("CAN-1", 1, null)], null,
        [new AffectedWorkOrder(Guid.NewGuid(), "WO-100", "Draft", "A"), new AffectedWorkOrder(Guid.NewGuid(), "WO-101", "Released", "A")]);

    private void ServeProduct(List<EngineeringChangeSummary> changes)
    {
        _server.OnJson("GET", "/api/products?q=", new List<ProductListItem> { new(_productId, "MAC-01", "Pressa idraulica", true, 1, 1) });
        _server.OnJson("GET", $"/api/products/{_productId}", new ProductDetail(_productId, "MAC-01", "Pressa idraulica", null, true,
            [new ProductBomItem(Guid.NewGuid(), "MORS-10", 2, null)], [new ProductRoutingStep(Guid.NewGuid(), 1, "Montaggio", null, null, 60)]));
        _server.OnJson("GET", $"/api/engineering/products/{_productId}/documents", new List<TechnicalDocument>
        {
            new(Guid.NewGuid(), _productId, 1, "schema", "Schema elettrico", "Schema potenza", "schema.pdf", "application/pdf", 2048, 3, "nuovo sezionatore", true, "Ufficio tecnico", DateTime.UtcNow),
        });
        _server.OnJson("GET", $"/api/engineering/changes?productId={_productId}", changes);
        _server.OnJson("GET", $"/api/engineering/products/{_productId}/revisions", new ProductRevisions("A", []));
    }

    [Fact]
    public async Task OnlyProduct_OpensAtOnce_WithItsDocumentsByPhase_ReadOnlyForTheShopFloor()
    {
        await LogInAsync("Operator");
        ServeProduct([]);

        var page = RenderComponent<Engineering>();

        page.WaitForAssertion(() => Assert.Contains("Schema potenza", page.Markup));
        Assert.Contains("1 · Montaggio", page.Markup);
        Assert.Contains("v3", page.Markup);
        Assert.DoesNotContain("Carica un documento", page.Markup);
        Assert.DoesNotContain("Ritira", page.Markup);
    }

    [Fact]
    public async Task ApprovedChange_ShowsTheBomDifference_AndIsAppliedFromThePage()
    {
        await LogInAsync("Management");
        var approved = Change("approved");
        ServeProduct([new EngineeringChangeSummary(approved.Id, 7, _productId, "MAC-01", "Pressa idraulica", approved.Title, "approved", "A", null, "Ufficio tecnico", DateTime.UtcNow, true, false)]);
        _server.OnJson("GET", $"/api/engineering/changes/{approved.Id}", approved);
        _server.OnJson("POST", $"/api/engineering/changes/{approved.Id}/apply", Change("applied") with { Id = approved.Id, ApplyReport = "Commesse in bozza aggiornate alla rev. B: WO-100." });

        var page = RenderComponent<Engineering>();
        page.WaitForAssertion(() => Assert.Contains("Schema potenza", page.Markup));
        page.FindAll("button").Single(b => b.TextContent == "Modifiche tecniche").Click();
        page.WaitForAssertion(() => Assert.Contains("MT 7", page.Markup));
        page.Find("tr.link").Click();

        page.WaitForAssertion(() => Assert.Contains("CAN-1", page.Markup));
        Assert.Contains("WO-101", page.Markup);
        page.FindAll("button").Single(b => b.TextContent.StartsWith("Applica")).Click();

        page.WaitForAssertion(() => Assert.Contains("Commesse in bozza aggiornate alla rev. B", page.Markup));
        Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath.EndsWith("/apply"));
    }
}
