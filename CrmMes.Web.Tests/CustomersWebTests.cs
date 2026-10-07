using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class CustomersWebTests : TestContext
{
    private readonly FakeServer _server = new();

    public CustomersWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/api/customers", new List<Customer>());
    }

    private async Task<IRenderedComponent<Customers>> OpenCreateFormAsync(string role, List<CustomFieldDefinition>? customFields = null)
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));
        _server.OnJson("GET", "/api/layout/customers.new/custom-fields", customFields ?? []);
        var page = RenderComponent<Customers>();
        page.WaitForAssertion(() => Assert.Contains("Nuovo cliente", page.Markup));
        page.FindAll("button").Single(b => b.TextContent == "Nuovo cliente").Click();
        return page;
    }

    [Fact]
    public async Task CustomField_RendersWithItsType_AndIsRequiredWhenConfigured()
    {
        var fieldId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Sales",
        [
            new CustomFieldDefinition(fieldId, "giorni-di-pagamento", "Giorni di pagamento", "Number", 1, true),
        ]);

        page.WaitForAssertion(() => Assert.Contains("Giorni di pagamento *", page.Markup));
        Assert.NotNull(page.Find("input[type=number]"));
    }

    [Fact]
    public async Task MissingRequiredCustomField_BlocksSubmission()
    {
        var fieldId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Sales",
        [
            new CustomFieldDefinition(fieldId, "tipologia-di-pagamento", "Tipologia di pagamento", "Text", 1, true),
        ]);
        page.WaitForAssertion(() => Assert.Contains("Tipologia di pagamento *", page.Markup));

        page.Find("input[placeholder='es. CLI-001']").Change("CLI-900");
        page.Find("input[placeholder='Nome o ragione sociale']").Change("Cliente prova");
        page.FindAll("button").Single(b => b.TextContent == "Crea cliente").Click();

        page.WaitForAssertion(() => Assert.Contains("Compila i campi obbligatori: Tipologia di pagamento", page.Markup));
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CustomFieldValue_IsSentWithTheCreateRequest()
    {
        var fieldId = Guid.NewGuid();
        var newId = Guid.NewGuid();
        var page = await OpenCreateFormAsync("Sales",
        [
            new CustomFieldDefinition(fieldId, "tipologia-di-pagamento", "Tipologia di pagamento", "Text", 1, false),
        ]);
        page.WaitForAssertion(() => Assert.Contains("Tipologia di pagamento", page.Markup));
        _server.OnJson("POST", "/api/customers", new Customer(newId, "CLI-900", "Cliente prova", null, null, null, null, null, true));

        page.Find("input[placeholder='es. CLI-001']").Change("CLI-900");
        page.Find("input[placeholder='Nome o ragione sociale']").Change("Cliente prova");
        page.Find("input[aria-label='Tipologia di pagamento']").Input("Bonifico 30gg");
        page.FindAll("button").Single(b => b.TextContent == "Crea cliente").Click();

        page.WaitForAssertion(() => Assert.EndsWith($"clienti/{newId}", Services.GetRequiredService<NavigationManager>().Uri));
        var post = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.PathAndQuery == "/api/customers");
        Assert.Contains("\"tipologia-di-pagamento\":\"Bonifico 30gg\"", post.Body);
    }
}
