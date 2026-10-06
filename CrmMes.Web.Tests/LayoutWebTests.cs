using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class LayoutWebTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _machineId = Guid.NewGuid();

    public LayoutWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    private static LayoutField F(string key, string label, int order, bool visible, bool required, bool canHide) =>
        new(key, label, order, visible, required, canHide) { DefaultLabel = label };

    private void ServeService(List<LayoutField> fields)
    {
        _server.OnJson("GET", "/api/service/requests?status=", new List<ServiceRequestRow>());
        _server.OnJson("GET", "/api/service/machines", new List<InstalledMachine>
        {
            new(_machineId, Guid.NewGuid(), "Officine", null, "Pressa", null, "SN-1", null, null, null, "Active", null, 0),
        });
        _server.OnJson("GET", "/api/layout/service.request", new LayoutScreen("service.request", fields));
    }

    [Fact]
    public async Task RequestForm_FollowsTheLayout_HidesFieldsAndAsksForRequiredOnes()
    {
        await LogInAsync("Sales");
        ServeService(
        [
            F("subject", "Titolo breve", 1, true, true, false),
            F("description", "Descrizione", 2, false, false, true),   // nascosto dall'Admin
            F("priority", "Priorità", 3, true, true, false),
            F("channel", "Canale", 4, true, true, false),
            F("requestedBy", "Chiamante", 5, true, true, true),         // obbligatorio
            F("contactInfo", "Contatto", 6, true, false, true),
        ]);

        var page = RenderComponent<Service>();
        page.WaitForAssertion(() => Assert.Contains("Titolo breve", page.Markup));

        Assert.DoesNotContain("Descrizione", page.Markup);
        Assert.Contains("Chiamante *", page.Markup);

        page.Find("select[aria-label='Macchina installata per la nuova richiesta']").Change(_machineId.ToString());
        page.Find("input[aria-label='Oggetto della richiesta']").Change("Allarme");
        page.FindAll("button").Single(b => b.TextContent == "Apri richiesta").Click();

        page.WaitForAssertion(() => Assert.Contains("Compila i campi obbligatori: Chiamante *.", page.Markup));
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task LayoutEditor_IsAdminOnly()
    {
        await LogInAsync("Operator");
        var page = RenderComponent<CrmMes.Web.Pages.Layout>();
        Assert.Contains("Solo amministratore", page.Markup);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task LayoutEditor_SavesTheChangedLabelAndOrder()
    {
        await LogInAsync("Admin");
        _server.OnJson("GET", "/api/layout/service.request", new LayoutScreen("service.request",
        [
            F("subject", "Oggetto", 1, true, true, false),
            F("description", "Descrizione", 2, true, false, true),
        ]));
        _server.OnJson("PUT", "/api/layout/service.request", new LayoutScreen("service.request",
        [
            F("description", "Guasto", 1, true, false, true),
            F("subject", "Oggetto", 2, true, true, false),
        ]));

        var page = RenderComponent<CrmMes.Web.Pages.Layout>();
        page.WaitForAssertion(() => Assert.Contains("Oggetto", page.Markup));
        page.Find("input[aria-label='Etichetta di Descrizione']").Change("Guasto");
        page.FindAll("button").Single(b => b.TextContent == "Salva layout").Click();

        page.WaitForAssertion(() => Assert.Contains("Layout salvato.", page.Markup));
        var put = _server.Requests.Single(r => r.Request.Method == HttpMethod.Put);
        Assert.Contains("\"fieldKey\":\"description\"", put.Body);
        Assert.Contains("\"label\":\"Guasto\"", put.Body);
    }
}
