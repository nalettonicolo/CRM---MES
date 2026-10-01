using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class ServiceWebTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _machineId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    public ServiceWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    private ServiceRequestRow Row(string status) => new(
        Guid.NewGuid(), 14, _machineId, "Quadro generale", "SN-01", _customerId, "Officine Aurora", "Allarme termico", null,
        "Urgente", status, "Phone", "Sig. Rossi", "333-1234567", DateTime.UtcNow, status == "Closed" ? DateTime.UtcNow : null, 0);

    private void ServeBase(List<ServiceRequestRow> rows)
    {
        _server.OnJson("GET", "/api/service/requests?status=", rows);
        _server.OnJson("GET", "/api/service/machines", new List<InstalledMachine>
        {
            new(_machineId, _customerId, "Officine Aurora", null, "Quadro generale", "QE-100", "SN-01", "Capannone 2", null, DateTime.Today.AddMonths(6), "Active", null, 1),
        });
    }

    [Fact]
    public async Task OpenRequests_AreListed_AndOperatorCannotOpenNewOnes()
    {
        await LogInAsync("Operator");
        ServeBase([Row("Open")]);

        var page = RenderComponent<Service>();

        page.WaitForAssertion(() => Assert.Contains("Allarme termico", page.Markup));
        Assert.Contains("Officine Aurora", page.Markup);
        Assert.DoesNotContain("Nuova richiesta", page.Markup);
    }

    [Fact]
    public async Task MachineWithWarrantyExpiringSoon_ShowsTheWarningBanner_AndAlreadyExpiredIsFlaggedDifferently()
    {
        await LogInAsync("Sales");
        var soonId = Guid.NewGuid();
        var expiredId = Guid.NewGuid();
        _server.OnJson("GET", "/api/service/requests?status=", new List<ServiceRequestRow>());
        _server.OnJson("GET", "/api/service/machines", new List<InstalledMachine>
        {
            new(soonId, _customerId, "Officine Aurora", null, "Pressa 120t", null, "SN-SOON", null, null, DateTime.Today.AddDays(10), "Active", null, 0),
            new(expiredId, _customerId, "Ferramenta Brianza", null, "Tornio CNC", null, "SN-OLD", null, null, DateTime.Today.AddDays(-5), "Active", null, 0),
        });

        var page = RenderComponent<Service>();
        page.WaitForAssertion(() => Assert.Contains("garanzia in scadenza entro 30 giorni", page.Markup));
        Assert.Contains("Pressa 120t", page.Markup);
        Assert.DoesNotContain("Tornio CNC (Ferramenta Brianza)", page.Markup); // expired, not "about to expire"

        await SelectMachinesTabAsync(page);
        Assert.Contains("in scadenza (10 gg)", page.Markup);
        Assert.Contains("scaduta", page.Markup);
    }

    private static async Task SelectMachinesTabAsync(IRenderedComponent<Service> page)
    {
        await page.InvokeAsync(() => page.FindAll("button.chip").Single(b => b.TextContent == "Macchine installate").Click());
    }

    [Fact]
    public async Task OpeningARequest_ShowsItsInterventions_AndRecordingOneMovesItInProgress()
    {
        await LogInAsync("Sales");
        var row = Row("Open");
        ServeBase([row]);
        var detail = new ServiceRequestDetail(row.Id, row.Number, _machineId, "Quadro generale", "SN-01", _customerId, "Officine Aurora",
            row.Subject, "Il quadro va in allarme dopo due ore", "Urgente", "Open", "Phone", "Sig. Rossi", "333-1234567", row.OpenedAt, null, []);
        _server.OnJson("GET", $"/api/service/requests/{row.Id}", detail);
        var afterIntervention = detail with
        {
            Status = "InProgress",
            Interventions = [new ServiceIntervention(Guid.NewGuid(), "Mario Bianchi", DateTime.UtcNow, DateTime.UtcNow, 2m, true, "Sostituito il relè", null, null)],
        };
        _server.OnJson("POST", $"/api/service/requests/{row.Id}/interventions", afterIntervention);

        var page = RenderComponent<Service>();
        page.WaitForAssertion(() => Assert.Contains("Allarme termico", page.Markup));
        page.Find("tr.link").Click();

        page.WaitForAssertion(() => Assert.Contains("RA 14", page.Markup));
        Assert.Contains("Registra intervento", page.Markup);

        page.FindAll("button").Single(b => b.TextContent == "Registra intervento").Click();

        page.WaitForAssertion(() => Assert.Contains("Mario Bianchi", page.Markup));
        Assert.Contains("In lavorazione", page.Markup);
    }
}
