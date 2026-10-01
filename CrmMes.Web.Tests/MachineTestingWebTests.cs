using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class MachineTestingWebTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly Guid _testId = Guid.NewGuid();

    public MachineTestingWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    private MachineDossier Dossier(string testStatus, string declarationStatus = "Draft", List<string>? missing = null) => new(
        _orderId, "WO-0042", "InProgress", "PI-200", "Pressa idraulica", "B", "Officine Rossi",
        new Manufacturer("Costruzioni Prova srl", "Via Roma 1, 20100 Milano", "01234567890"),
        [new MachineTest(_testId, 3, "FAT", testStatus, "M-77", "Stabilimento", DateTime.UtcNow.Date, null, null, "Prova", DateTime.UtcNow,
            testStatus == "Draft" ? null : DateTime.UtcNow, testStatus == "Draft" ? null : "Collaudatore",
            [
                new MachineTestItem(1, "Sicurezza elettrica (EN 60204-1)", "Resistenza d'isolamento (18.3)", "≥ 1 MΩ a 500 V c.c.", null, null, null),
                new MachineTestItem(2, "Dispositivi di sicurezza", "Arresto di emergenza", "Conforme", null, null, null),
            ])],
        [new TechnicalFileItem("risk-assessment", "Valutazione dei rischi", false, "Present", "UT/rischi.pdf", "Prova", DateTime.UtcNow),
         new TechnicalFileItem("partly-completed", "Quasi-macchine incorporate", true, null, null, null, null)],
        false,
        new MachineDeclaration(true, declarationStatus, declarationStatus == "Issued" ? 5 : null, "2006/42/CE",
            "Direttiva 2006/42/CE del Parlamento europeo e del Consiglio, del 17 maggio 2006, relativa alle macchine",
            "Pressa idraulica", "Stampaggio", "PI-200", null, "M-77", 2026, "Direttiva 2014/30/UE", "EN ISO 12100:2010\nEN 60204-1:2018", null,
            "Mario Rossi, Milano", "Milano", "Mario Rossi", "Legale rappresentante", null,
            declarationStatus == "Issued" ? DateTime.UtcNow : null, declarationStatus == "Issued" ? "Prova" : null),
        missing ?? []);

    [Fact]
    public async Task List_ShowsTestsFileAndDeclarationState()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", "/api/machine-testing/work-orders?search=", new List<MachineTestingSummary>
        {
            new(_orderId, "WO-0042", "InProgress", "PI-200", "Pressa idraulica", "Officine Rossi", 2, "SAT", "Passed", 11, 11, "Issued", 5),
        });

        var page = RenderComponent<MachineTesting>();

        page.WaitForAssertion(() => Assert.Contains("WO-0042", page.Markup));
        Assert.Contains("SAT Superato", page.Markup);
        Assert.Contains("11 di 11", page.Markup);
        Assert.Contains("Emessa n. 5", page.Markup);
    }

    [Fact]
    public async Task OpenTest_IsFilledAtTheShopFloor_AndClosedAfterConfirmation()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", $"/api/machine-testing/work-orders/{_orderId}", Dossier("Draft"));
        _server.OnJson("PUT", $"/api/machine-testing/tests/{_testId}", Dossier("Draft"));
        _server.OnJson("POST", $"/api/machine-testing/tests/{_testId}/close", Dossier("Passed"));

        var page = RenderComponent<MachineTestingPage>(p => p.Add(x => x.Id, _orderId));

        page.WaitForAssertion(() => Assert.Contains("Resistenza d'isolamento (18.3)", page.Markup));
        var count = page.FindAll("select[aria-label='Esito']").Count;
        for (var i = 0; i < count; i++)
        {
            page.FindAll("select[aria-label='Esito']")[i].Change("Pass");
        }

        page.FindAll("button").Single(b => b.TextContent == "Chiudi collaudo").Click();
        page.WaitForAssertion(() => Assert.Contains("Dopo la chiusura non si modifica più", page.Markup));
        page.FindAll("button").First(b => b.TextContent == "Chiudi collaudo").Click(); // the confirmation sits above the test

        page.WaitForAssertion(() => Assert.Contains("Collaudo chiuso.", page.Markup));
        var saved = _server.Requests.Single(r => r.Request.Method == HttpMethod.Put);
        Assert.Contains("\"result\":\"Pass\"", saved.Body);
        Assert.Contains("\"testDate\":\"" + DateTime.UtcNow.Date.ToString("yyyy-MM-dd") + "\"", saved.Body);
        Assert.Contains("Superato", page.Markup);
        Assert.DoesNotContain(">Riapri<", page.Markup);

        // The shop floor reads the technical file but doesn't edit it.
        page.FindAll("button").Single(b => b.TextContent == "Fascicolo tecnico").Click();
        Assert.Contains("UT/rischi.pdf", page.Markup);
        Assert.Empty(page.FindAll("select[aria-label=\"Stato dell'elemento\"]"));
    }

    [Fact]
    public async Task Declaration_ListsWhatIsMissing_AndOnceIssuedCanBePrinted()
    {
        await LogInAsync("Management");
        _server.OnJson("GET", $"/api/machine-testing/work-orders/{_orderId}", Dossier("Passed", missing: ["il fascicolo tecnico non è completo"]));

        var page = RenderComponent<MachineTestingPage>(p => p.Add(x => x.Id, _orderId));
        page.WaitForAssertion(() => Assert.Contains("WO-0042", page.Markup));
        page.FindAll("button").Single(b => b.TextContent == "Dichiarazione CE").Click();

        Assert.Contains("il fascicolo tecnico non è completo", page.Markup);
        Assert.True(page.FindAll("button").Single(b => b.TextContent == "Emetti la dichiarazione").HasAttribute("disabled"));
        Assert.Contains("Dichiarazione CE di conformità", page.Markup);
        Assert.Contains("EN 60204-1:2018", page.Markup);

        // Issued: frozen, numbered, printable.
        _server.OnJson("GET", $"/api/machine-testing/work-orders/{_orderId}", Dossier("Passed", "Issued"));
        var issued = RenderComponent<MachineTestingPage>(p => p.Add(x => x.Id, _orderId));
        issued.WaitForAssertion(() => Assert.Contains("WO-0042", issued.Markup));
        issued.FindAll("button").Single(b => b.TextContent == "Dichiarazione CE").Click();
        Assert.Contains("Dichiarazione n. 5", issued.Markup);
        Assert.DoesNotContain("Emetti la dichiarazione", issued.Markup);
        issued.FindAll("button").Single(b => b.TextContent == "Stampa").Click();
        JSInterop.VerifyInvoke("print");
    }
}
