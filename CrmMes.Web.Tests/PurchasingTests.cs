using System.Net;
using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class PurchasingTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly Guid _cableLine = Guid.NewGuid();
    private readonly Guid _breakerLine = Guid.NewGuid();

    public PurchasingTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/api/suppliers", new[] { new Supplier(_supplierId, "Elettroforniture spa", "EF01", null, null, null, true) });
    }

    private PurchaseOrder Order(string status, decimal cableReceived = 0, DateTime? expected = null) => new(
        _orderId, "PO-2026-0007", _supplierId, status, DateTime.UtcNow.AddDays(-5), DateTime.UtcNow.AddDays(-4), null, expected,
        [
            new PurchaseOrderItem(_cableLine, "CAV-FG16", "Cavo FG16 3x2,5", 500, 0.9m, cableReceived, null),
            new PurchaseOrderItem(_breakerLine, "INT-MT", "Interruttore 16A", 20, 12m, 0, null),
        ]);

    private async Task<IRenderedComponent<PurchaseOrderPage>> OpenAsync(string role, PurchaseOrder order)
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));
        _server.OnJson("GET", $"/api/procurement/purchase-orders/{_orderId}", order);
        var page = RenderComponent<PurchaseOrderPage>(p => p.Add(x => x.Id, _orderId));
        page.WaitForAssertion(() => Assert.Contains("PO-2026-0007", page.Markup));
        return page;
    }

    [Fact]
    public async Task Warehouse_ReceivesPartOfTheDelivery_WithTheSuppliersLot()
    {
        _server.OnJson("POST", $"/api/procurement/purchase-orders/{_orderId}/receive", Order("PartiallyReceived", cableReceived: 500));
        var page = await OpenAsync("Warehouse", Order("Confirmed"));
        Assert.Contains("Elettroforniture spa", page.Markup);

        page.FindAll("button").Single(b => b.TextContent == "Ricevi merce").Click();
        Assert.Equal("500", page.Find("input[aria-label='Quantità arrivata di CAV-FG16']").GetAttribute("value"));
        page.Find("input[aria-label='Lotto di CAV-FG16']").Change("L-EF-7781");
        page.Find("input[aria-label='Quantità arrivata di INT-MT']").Change("");
        page.FindAll("button").Single(b => b.TextContent == "Registra l'arrivo").Click();

        page.WaitForAssertion(() => Assert.Contains("L'ordine resta aperto per il resto", page.Markup));
        var body = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post).Body;
        Assert.Contains(_cableLine.ToString(), body);
        Assert.Contains("\"lotNumber\":\"L-EF-7781\"", body);
        Assert.DoesNotContain(_breakerLine.ToString(), body);
        Assert.Contains("Completa", page.Markup);     // the cable line, all 500 in
        Assert.Contains("Da ricevere", page.Markup);  // the breakers, still to come
    }

    [Fact]
    public async Task ReceivingMoreThanOrdered_IsStoppedOnThePage()
    {
        var page = await OpenAsync("Admin", Order("Confirmed"));
        page.FindAll("button").Single(b => b.TextContent == "Ricevi merce").Click();

        page.Find("input[aria-label='Quantità arrivata di INT-MT']").Change("25");
        page.FindAll("button").Single(b => b.TextContent == "Registra l'arrivo").Click();

        Assert.Contains("mancano solo 20", page.Find("[role=alert]").TextContent);
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task OnlyPurchasingConfirms_AndLateDeliveriesAreFlagged()
    {
        var draft = await OpenAsync("Warehouse", Order("Draft"));
        Assert.DoesNotContain("Conferma l'ordine", draft.Markup);

        var late = await OpenAsync("Purchasing", Order("Confirmed", expected: DateTime.UtcNow.AddDays(-3)));
        Assert.Contains("in ritardo", late.Markup);
        Assert.Contains("Ricevi merce", late.Markup);
    }

    [Fact]
    public void Rules_FollowTheApiPolicies()
    {
        Assert.True(PurchasingRules.CanReceive(Order("PartiallyReceived", 100), "Warehouse"));
        Assert.False(PurchasingRules.CanReceive(Order("Draft"), "Admin"));
        Assert.False(PurchasingRules.CanReceive(Order("Confirmed"), "Sales"));
        Assert.True(PurchasingRules.CanConfirm(Order("Draft"), "Purchasing"));
        Assert.False(PurchasingRules.CanConfirm(Order("Draft"), "Warehouse"));
        Assert.Equal("Scorta minima", Labels.MissingSource("MinStock"));
    }
}
