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
        _server.OnJson("GET", "/api/layout/service.request", new LayoutScreen("service.request", "Nuova richiesta di assistenza (Service)", fields));
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
    public async Task LayoutEditor_IsReadOnly_ForRolesNotAuthorisedByTheAdmin()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", "/api/layout/access", new LayoutAccess(false, [], ["Management", "Sales"]));

        var page = RenderComponent<CrmMes.Web.Pages.Layout>();
        page.WaitForAssertion(() => Assert.Contains("Sola lettura", page.Markup));
        Assert.DoesNotContain("Salva layout", page.Markup);
        Assert.DoesNotContain("Chi può modificare i layout", page.Markup);
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task LayoutEditor_IsOpenToRolesAuthorisedByTheAdmin()
    {
        await LogInAsync("Sales");
        _server.OnJson("GET", "/api/layout/access", new LayoutAccess(true, ["Sales"], ["Management", "Sales"]));
        _server.OnJson("GET", "/api/layout/service.request", new LayoutScreen("service.request", "Nuova richiesta di assistenza (Service)",
        [
            F("subject", "Oggetto", 1, true, true, false),
        ]));

        var page = RenderComponent<CrmMes.Web.Pages.Layout>();
        page.WaitForAssertion(() => Assert.Contains("Salva layout", page.Markup));
        Assert.DoesNotContain("Chi può modificare i layout", page.Markup);
    }

    [Fact]
    public async Task LayoutEditor_SavesTheChangedLabelAndOrder()
    {
        await LogInAsync("Admin");
        _server.OnJson("GET", "/api/layout/access", new LayoutAccess(true, [], ["Management", "Sales"]));
        _server.OnJson("GET", "/api/layout/service.request", new LayoutScreen("service.request", "Nuova richiesta di assistenza (Service)",
        [
            F("subject", "Oggetto", 1, true, true, false),
            F("description", "Descrizione", 2, true, false, true),
        ]));
        _server.OnJson("PUT", "/api/layout/service.request", new LayoutScreen("service.request", "Nuova richiesta di assistenza (Service)",
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

    [Fact]
    public async Task CustomFields_AreListed_AndANewOnePostsToTheApi()
    {
        await LogInAsync("Admin");
        _server.OnJson("GET", "/api/layout/access", new LayoutAccess(true, [], []));
        _server.OnJson("GET", "/api/layout/service.request", new LayoutScreen("service.request", "Nuova richiesta di assistenza (Service)",
        [
            F("subject", "Oggetto", 1, true, true, false),
        ]));
        var existingId = Guid.NewGuid();
        _server.OnJson("GET", "/api/layout/service.request/custom-fields", new List<CustomFieldDefinition>
        {
            new(existingId, "giorni-di-pagamento", "Giorni di pagamento", "Number", 1, true),
        });
        var createdId = Guid.NewGuid();
        _server.OnJson("POST", "/api/layout/service.request/custom-fields",
            new CustomFieldDefinition(createdId, "note-interne", "Note interne", "Text", 2, false));

        var page = RenderComponent<CrmMes.Web.Pages.Layout>();
        page.WaitForAssertion(() => Assert.Contains("Giorni di pagamento", page.Markup));
        Assert.Contains("Numero", page.Markup);

        page.Find("input[placeholder='es. Giorni di pagamento']").Change("Note interne");
        page.FindAll("button").Single(b => b.TextContent == "Aggiungi campo").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r =>
            r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.PathAndQuery == "/api/layout/service.request/custom-fields"));
        var post = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post);
        Assert.Contains("\"label\":\"Note interne\"", post.Body);
    }

    [Fact]
    public async Task DeletingACustomField_SendsTheDeleteRequest()
    {
        await LogInAsync("Admin");
        _server.OnJson("GET", "/api/layout/access", new LayoutAccess(true, [], []));
        _server.OnJson("GET", "/api/layout/service.request", new LayoutScreen("service.request", "Nuova richiesta di assistenza (Service)",
        [
            F("subject", "Oggetto", 1, true, true, false),
        ]));
        var fieldId = Guid.NewGuid();
        _server.OnJson("GET", "/api/layout/service.request/custom-fields", new List<CustomFieldDefinition>
        {
            new(fieldId, "note-interne", "Note interne", "Text", 1, false),
        });
        _server.On("DELETE", $"/api/layout/service.request/custom-fields/{fieldId}", _ => FakeServer.Json(new { }, System.Net.HttpStatusCode.NoContent));

        var page = RenderComponent<CrmMes.Web.Pages.Layout>();
        page.WaitForAssertion(() => Assert.Contains("Note interne", page.Markup));

        page.FindAll("button").Single(b => b.TextContent == "Elimina").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Delete));
    }
}
