using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class HaccpWebTests : TestContext
{
    private readonly FakeServer _server = new();

    public HaccpWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();

        _server.OnJson("GET", "/api/haccp/templates", new List<HaccpTemplate>
        {
            new("cottura", "Cottura e termo-trattamento", "Forni e marmitte.",
                [new("Temperatura al cuore a fine cottura", "Cottura", "Patogeni", "°C", 72m, null, "Ogni lotto", "Prolungare la cottura")]),
            new("sanificazione", "Pulizia e sanificazione", "Superfici a contatto.",
                [new("Pulizia e sanificazione delle superfici a contatto", "Reparto", "Contaminazione", null, null, null, "Fine turno", "Ripetere")]),
        });
        _server.OnJson("GET", "/api/haccp/control-points", new List<HaccpControlPoint>());
        _server.OnJson("GET", $"/api/haccp/readings?from={DateTime.UtcNow.Date.AddDays(-14):yyyy-MM-dd}&to={DateTime.UtcNow.Date:yyyy-MM-dd}",
            new List<HaccpReading>());
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    [Fact]
    public async Task Warehouse_AppliesAModelAfterConfirmation_AndSeesTheCount()
    {
        await LogInAsync("Warehouse");
        _server.OnJson("POST", "/api/haccp/templates/cottura/apply", new ApplyHaccpTemplateResult("cottura", 1, 0));
        var page = RenderComponent<Haccp>();
        page.WaitForAssertion(() => Assert.Contains("Cottura e termo-trattamento", page.Markup));

        page.FindAll("button").First(b => b.TextContent.Trim() == "Applica").Click();
        Assert.Contains("Aggiungo il modello al registro?", page.Markup);

        page.FindAll("button").First(b => b.TextContent.Trim() == "Conferma").Click();

        page.WaitForAssertion(() => Assert.Contains("Modello applicato: aggiunti 1, già presenti 0.", page.Markup));
        Assert.Single(_server.Requests, r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath == "/api/haccp/templates/cottura/apply");
    }

    [Fact]
    public async Task Operator_SeesTheModels_ButCannotApplyThem()
    {
        await LogInAsync("Operator");
        var page = RenderComponent<Haccp>();

        page.WaitForAssertion(() => Assert.Contains("Cottura e termo-trattamento", page.Markup));
        Assert.Contains("Solo magazzino e amministrazione", page.Markup);
        Assert.Empty(page.FindAll("button").Where(b => b.TextContent.Trim() == "Applica"));
    }
}
