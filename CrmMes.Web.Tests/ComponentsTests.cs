using System.Net;
using Bunit;
using CrmMes.Web.Components;
using CrmMes.Web.Layout;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

/// <summary>Pages rendered with bUnit against an in-memory server: what the user sees in each state.</summary>
public class ComponentsTests : TestContext
{
    private readonly FakeServer _server = new();

    public ComponentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private async Task LogInAsync(string role = "Admin")
    {
        var session = Services.GetRequiredService<Session>();
        await session.SetAsync(new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));
    }

    [Fact]
    public void Login_ShowsTheReason_WhenTheAdminKeepsTheRoleOffTheWeb()
    {
        _server.OnJson("GET", "/ping", new { status = "ok" });
        _server.OnJson("POST", "/api/auth/login", new { message = "Il tuo ruolo non può accedere dalla piattaforma web. Chiedi all'amministratore." },
            HttpStatusCode.Forbidden);
        var login = RenderComponent<Login>();

        login.Find("input[type=email]").Change("anna@example.test");
        login.Find("input[type=password]").Change("prova");
        login.Find("form").Submit();

        login.WaitForAssertion(() => Assert.Contains("Chiedi all'amministratore", login.Find("[role=alert]").TextContent));
    }

    [Fact]
    public void Login_AsksForBothFields()
    {
        var login = RenderComponent<Login>();

        login.Find("form").Submit();

        Assert.Contains("Inserisci email e password", login.Find("[role=alert]").TextContent);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task WorkOrders_ShowOpenOrders_WithCopyableCodes()
    {
        await LogInAsync();
        _server.OnJson("GET", "/api/work-orders?status=Open", new[]
        {
            new WorkOrderSummary(Guid.NewGuid(), "C-2026-0042", "L-0042", Guid.NewGuid(), "QE-GEN", "Quadro generale", 2, "InProgress",
                null, DateTime.UtcNow, 4, 2),
        });

        var page = RenderComponent<WorkOrders>();

        page.WaitForAssertion(() => Assert.Contains("C-2026-0042", page.Markup));
        Assert.Contains("In lavorazione", page.Markup);
        Assert.Contains("2/4 fasi", page.Markup);
        Assert.Equal(2, page.FindAll("button[aria-label^='Copia']").Count);
    }

    [Fact]
    public async Task WorkOrders_SearchByLastDigits_UsesTheLookup()
    {
        await LogInAsync();
        _server.OnJson("GET", "/api/work-orders?status=Open", Array.Empty<WorkOrderSummary>());
        _server.OnJson("GET", "/api/work-orders/lookup?q=0042", new[]
        {
            new WorkOrderLookup(Guid.NewGuid(), "C-2026-0042", "L-0042", "Quadro generale", 2, "Released", null, "Condominio Aurora", null),
        });
        var page = RenderComponent<WorkOrders>();
        page.WaitForAssertion(() => Assert.Contains("Nessuna commessa in questo stato", page.Markup));

        page.Find("input[type=search]").Input("0042");

        page.WaitForAssertion(() => Assert.Contains("C-2026-0042", page.Markup), TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task Materials_ShowTheErrorAndARetry_WhenTheServerFails()
    {
        await LogInAsync();
        _server.OnJson("GET", "/api/materials?belowMinimumOnly=false", new { message = "Database non raggiungibile." }, HttpStatusCode.ServiceUnavailable);

        var page = RenderComponent<Materials>();

        page.WaitForAssertion(() => Assert.Contains("Database non raggiungibile.", page.Markup));
        Assert.Contains("Riprova", page.Markup);
    }

    [Fact]
    public async Task AccessSettings_AreOnlyForTheAdmin()
    {
        await LogInAsync("Sales");

        var page = RenderComponent<AccessSettings>();

        Assert.Contains("Solo per l'amministratore", page.Markup);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task AccessSettings_SaveOnlyTheRestrictions()
    {
        await LogInAsync();
        var settings = new AccessChannels(["desktop", "web", "mobile"], [], [],
            [new AccessArea("production", "Produzione e commesse", null)], ["Admin", "Sales"]);
        _server.OnJson("GET", "/api/company-profile/access", settings);
        _server.OnJson("PUT", "/api/company-profile/access", settings);
        _server.OnJson("GET", "/api/company-profile/areas?channel=web", new[] { "production" });
        var page = RenderComponent<AccessSettings>();
        page.WaitForAssertion(() => Assert.Contains("Commerciale", page.Markup));

        page.Find("input[aria-label='Commerciale: Desktop']").Change(false);
        page.Find("button:not(.chip)").Click();

        page.WaitForAssertion(() => Assert.Contains("Impostazioni salvate", page.Markup));
        Assert.DoesNotContain("class=\"alert\"", page.Markup);
        Assert.Equal(["production"], Services.GetRequiredService<Session>().WebAreas);
        var body = _server.Requests.Single(r => r.Request.Method == HttpMethod.Put).Body;
        Assert.Contains("\"Sales\":[\"web\",\"mobile\"]", body);
        Assert.DoesNotContain("Admin", body);
    }

    [Fact]
    public void CopyCode_ShowsTheCodeAsText()
    {
        var copy = RenderComponent<CopyCode>(parameters => parameters.Add(p => p.Value, "L-2026-0007"));

        Assert.Equal("L-2026-0007", copy.Find(".code").TextContent);
    }
}
