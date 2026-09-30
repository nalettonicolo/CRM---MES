using System.Net;
using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class QuoteRulesTests
{
    private static Quote Make(string status, DateTime? convertedAt = null, bool withProduct = true, DateTime? validUntil = null) =>
        new(Guid.NewGuid(), "PR-1", Guid.NewGuid(), "Condominio Aurora", "C-AUR", status, validUntil, null, DateTime.UtcNow,
            null, null, null, convertedAt, 100,
            [new QuoteItem(Guid.NewGuid(), 1, withProduct ? Guid.NewGuid() : null, null, null, "Riga", 1, 100, 0, 100)]);

    [Fact]
    public void CommercialSteps_BelongToSales_AndFollowTheStatus()
    {
        Assert.True(QuoteRules.CanSend(Make("Draft"), "Sales"));
        Assert.False(QuoteRules.CanSend(Make("Sent"), "Sales"));
        Assert.True(QuoteRules.CanDecide(Make("Sent"), "Admin"));
        Assert.False(QuoteRules.CanDecide(Make("Accepted"), "Sales"));
        Assert.False(QuoteRules.CanDecide(Make("Draft"), "Warehouse"));
    }

    [Fact]
    public void WorkOrders_ComeFromAcceptedQuotesWithProducts_Once()
    {
        Assert.True(QuoteRules.CanTurnIntoWorkOrders(Make("Accepted"), "Warehouse"));
        Assert.False(QuoteRules.CanTurnIntoWorkOrders(Make("Accepted", DateTime.UtcNow), "Sales"));
        Assert.False(QuoteRules.CanTurnIntoWorkOrders(Make("Accepted", withProduct: false), "Sales"));
        Assert.False(QuoteRules.CanTurnIntoWorkOrders(Make("Sent"), "Admin"));
        Assert.False(QuoteRules.CanTurnIntoWorkOrders(Make("Accepted"), "Operator"));
    }

    [Fact]
    public void Expiry_OnlyMattersBeforeTheCustomerDecides()
    {
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(QuoteRules.IsExpired(Make("Sent", validUntil: now.AddDays(-3)), now));
        Assert.False(QuoteRules.IsExpired(Make("Accepted", validUntil: now.AddDays(-3)), now));
        Assert.Equal("Convertito", Labels.QuoteStatus("Accepted", now));
        Assert.Equal("Inviato", Labels.QuoteStatus("Sent"));
    }
}

public class QuotePageTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _id = Guid.NewGuid();

    public QuotePageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Quote Make(string status, DateTime? convertedAt = null) =>
        new(_id, "PR-2026-0012", Guid.NewGuid(), "Condominio Aurora", "C-AUR", status, null, "Consegna 4 settimane", DateTime.UtcNow,
            null, null, null, convertedAt, 9120,
            [
                new QuoteItem(Guid.NewGuid(), 1, Guid.NewGuid(), "QE-GEN", "Quadro generale", "Quadro generale BT", 2, 4800, 5, 9120),
                new QuoteItem(Guid.NewGuid(), 2, null, null, null, "Trasporto", 1, 0, 0, 0),
            ]);

    private async Task<IRenderedComponent<QuotePage>> OpenAsync(string role, Quote quote)
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));
        _server.OnJson("GET", $"/api/quotes/{_id}", quote);
        var page = RenderComponent<QuotePage>(parameters => parameters.Add(p => p.Id, _id));
        page.WaitForAssertion(() => Assert.Contains("PR-2026-0012", page.Markup));
        return page;
    }

    [Fact]
    public async Task Sales_ConfirmsAcceptance_BeforeItIsSent()
    {
        _server.OnJson("POST", $"/api/quotes/{_id}/accept", Make("Accepted"));
        var page = await OpenAsync("Sales", Make("Sent"));
        Assert.Contains("Inviato", page.Markup);

        page.FindAll("button").Single(b => b.TextContent == "Accettato dal cliente").Click();
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
        Assert.Contains("non si potrà più modificare", page.Markup);

        page.FindAll("button").Last(b => b.TextContent == "Accettato dal cliente").Click();

        page.WaitForAssertion(() => Assert.Contains("Crea le commesse", page.Markup));
        Assert.Single(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Conversion_ShowsLinksToTheNewWorkOrders()
    {
        var workOrderId = Guid.NewGuid();
        _server.OnJson("POST", $"/api/quotes/{_id}/convert",
            new ConvertQuoteResult(_id, "PR-2026-0012", [new ConvertedWorkOrder(workOrderId, "WO-0042", "QE-GEN", 2)]));
        var page = await OpenAsync("Warehouse", Make("Accepted"));

        page.FindAll("button").Single(b => b.TextContent == "Crea le commesse").Click();
        Assert.Contains("una commessa in bozza per ogni riga con un prodotto (1)", page.Markup);
        _server.OnJson("GET", $"/api/quotes/{_id}", Make("Accepted", DateTime.UtcNow));
        page.FindAll("button").Last(b => b.TextContent == "Crea le commesse").Click();

        page.WaitForAssertion(() => Assert.Contains($"commesse/{workOrderId}", page.Markup));
        Assert.Contains("Convertito", page.Markup);
    }

    [Fact]
    public async Task Operator_SeesTheQuote_WithoutActions()
    {
        var page = await OpenAsync("Operator", Make("Sent"));

        Assert.Contains("9.120,00", page.Markup);
        Assert.Empty(page.FindAll("div.actions button"));
    }

    [Fact]
    public async Task ServerRefusal_IsShownOnThePage()
    {
        _server.OnJson("POST", $"/api/quotes/{_id}/send", new { message = "Un preventivo nello stato 'Sent' non può essere inviato al cliente." }, HttpStatusCode.Conflict);
        var page = await OpenAsync("Admin", Make("Draft"));

        page.FindAll("button").Single(b => b.TextContent == "Segna come inviato").Click();

        page.WaitForAssertion(() => Assert.Contains("non può essere inviato", page.Find("[role=alert]").TextContent));
    }
}
