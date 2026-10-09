using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

/// <summary>Il modulo "Nuovo materiale" segue lo Strumento Layout (etichette/ordine/campi personalizzati),
/// come già fatto per fornitori e clienti. Vedi CustomersWebTests per lo stesso schema.</summary>
public class MaterialsWebTests : TestContext
{
    private readonly FakeServer _server = new();

    public MaterialsWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/api/materials?belowMinimumOnly=false", new List<Material>());
    }

    private async Task<IRenderedComponent<Materials>> OpenCreateFormAsync(string role, List<CustomFieldDefinition>? customFields = null)
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));
        _server.OnJson("GET", "/api/layout/materials.new/custom-fields", customFields ?? []);
        var page = RenderComponent<Materials>();
        page.WaitForAssertion(() => Assert.Contains("Nuovo materiale", page.Markup));
        page.FindAll("button").Single(b => b.TextContent == "Nuovo materiale").Click();
        return page;
    }

    [Fact]
    public async Task CustomField_RendersWithItsType_AndIsRequiredWhenConfigured()
    {
        var fieldId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Warehouse",
        [
            new CustomFieldDefinition(fieldId, "lotto-fornitore-obbligatorio", "Lotto fornitore obbligatorio", "Checkbox", 1, true),
        ]);

        page.WaitForAssertion(() => Assert.Contains("Lotto fornitore obbligatorio *", page.Markup));
        Assert.NotNull(page.Find("input[type=checkbox]"));
    }

    [Fact]
    public async Task MissingRequiredCustomField_BlocksSubmission()
    {
        var fieldId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Warehouse",
        [
            new CustomFieldDefinition(fieldId, "fornitore-preferito", "Fornitore preferito", "Text", 1, true),
        ]);
        page.WaitForAssertion(() => Assert.Contains("Fornitore preferito *", page.Markup));

        page.Find("input[placeholder='es. VIT-M6']").Change("VIT-900");
        page.Find("input[placeholder='Descrizione articolo']").Change("Vite di prova");
        page.FindAll("button").Single(b => b.TextContent == "Crea materiale").Click();

        page.WaitForAssertion(() => Assert.Contains("Compila i campi obbligatori: Fornitore preferito", page.Markup));
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CustomFieldValue_IsSentWithTheCreateRequest()
    {
        var fieldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Warehouse",
        [
            new CustomFieldDefinition(fieldId, "fornitore-preferito", "Fornitore preferito", "Text", 1, false),
        ]);
        page.WaitForAssertion(() => Assert.Contains("Fornitore preferito", page.Markup));
        _server.OnJson("POST", "/api/materials", new Material(newId, "VIT-900", "Vite di prova", "pz", 0, 0, true, false));

        page.Find("input[placeholder='es. VIT-M6']").Change("VIT-900");
        page.Find("input[placeholder='Descrizione articolo']").Change("Vite di prova");
        page.Find("input[aria-label='Fornitore preferito']").Input("ACME srl");
        page.FindAll("button").Single(b => b.TextContent == "Crea materiale").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post));
        var post = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.PathAndQuery == "/api/materials");
        Assert.Contains("\"fornitore-preferito\":\"ACME srl\"", post.Body);
    }
}
