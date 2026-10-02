using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class EnergyWebTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _equipmentId = Guid.NewGuid();

    public EnergyWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    [Fact]
    public async Task ProjectWithoutAfterPeriod_ShowsBaselineOnly_AndOperatorCannotManage()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", "/api/equipment", new List<Equipment> { new(_equipmentId, "Forno 1", "FR-01", null, null, true) });
        _server.OnJson("GET", "/api/energy/projects", new List<EnergyProject>
        {
            new(Guid.NewGuid(), _equipmentId, "Forno 1", "FR-01", "Sostituzione forno", null,
                DateTime.Today.AddMonths(-2), DateTime.Today.AddMonths(-1), 1000m, 12, null, null, null, null, null, null, "Ufficio tecnico", DateTime.UtcNow),
        });

        var page = RenderComponent<Energy>();

        page.WaitForAssertion(() => Assert.Contains("Sostituzione forno", page.Markup));
        Assert.Contains("da definire", page.Markup);
        Assert.DoesNotContain("Nuovo progetto", page.Markup);
        Assert.DoesNotContain("Imposta ex post", page.Markup);
    }

    [Fact]
    public async Task ProjectWithSavings_ShowsThePercentage_AndManagementCanSetAfterPeriod()
    {
        await LogInAsync("Management");
        var projectId = Guid.NewGuid();
        _server.OnJson("GET", "/api/equipment", new List<Equipment> { new(_equipmentId, "Forno 1", "FR-01", null, null, true) });
        var project = new EnergyProject(projectId, _equipmentId, "Forno 1", "FR-01", "Sostituzione forno", null,
            DateTime.Today.AddMonths(-2), DateTime.Today.AddMonths(-1), 1000m, 12, null, null, null, null, null, null, "Admin", DateTime.UtcNow);
        _server.OnJson("GET", "/api/energy/projects", new List<EnergyProject> { project });

        var page = RenderComponent<Energy>();
        page.WaitForAssertion(() => Assert.Contains("Imposta ex post", page.Markup));

        page.FindAll("button").Single(b => b.TextContent == "Imposta ex post").Click();
        page.WaitForAssertion(() => Assert.Contains("Periodo ex post", page.Markup));

        var updated = project with { AfterFrom = DateTime.Today.AddDays(-30), AfterTo = DateTime.Today, AfterKwh = 600m, AfterReadingCount = 10, SavingsPercent = 40.0m };
        _server.OnJson("PUT", $"/api/energy/projects/{projectId}/after-period", updated);
        _server.OnJson("GET", "/api/energy/projects", new List<EnergyProject> { updated });

        page.FindAll("button").Single(b => b.TextContent == "Salva periodo").Click();

        page.WaitForAssertion(() => Assert.Contains("-40", page.Markup));
        Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Put && r.Request.RequestUri!.AbsolutePath.EndsWith("/after-period"));
    }

    [Fact]
    public async Task WorkOrderConsumption_ShowsTheBreakdownByMachine()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", "/api/equipment", new List<Equipment>());
        _server.OnJson("GET", "/api/energy/projects", new List<EnergyProject>());
        _server.OnJson("GET", "/api/energy/work-orders/WO-100/consumption", new WorkOrderEnergy("WO-100", 130m,
        [
            new WorkOrderEnergyByEquipment(Guid.NewGuid(), "Pressa 120t", 100m),
            new WorkOrderEnergyByEquipment(Guid.NewGuid(), "Forno", 30m),
        ]));

        var page = RenderComponent<Energy>();
        page.WaitForAssertion(() => Assert.Contains("Consumo di una commessa", page.Markup));

        var input = page.Find("input[placeholder^='es. WO-']");
        input.Change("WO-100");
        page.FindAll("button").Last(b => b.TextContent == "Calcola consumo").Click();

        page.WaitForAssertion(() => Assert.Contains("130", page.Markup));
        Assert.Contains("Pressa 120t: 100", page.Markup);
        Assert.Contains("Forno: 30", page.Markup);
    }

    [Fact]
    public async Task CreatingAProject_PicksTheMachineFromTheDropdown_AndSendsItsRealId()
    {
        await LogInAsync("Admin");
        _server.OnJson("GET", "/api/equipment", new List<Equipment> { new(_equipmentId, "Forno 1", "FR-01", null, null, true) });
        _server.OnJson("GET", "/api/energy/projects", new List<EnergyProject>());
        var created = new EnergyProject(Guid.NewGuid(), _equipmentId, "Forno 1", "FR-01", "Sostituzione forno", null,
            DateTime.Today.AddMonths(-1), DateTime.Today, 1000m, 10, null, null, null, null, null, null, "Admin", DateTime.UtcNow);
        _server.OnJson("POST", "/api/energy/projects", created);

        var page = RenderComponent<Energy>();
        page.WaitForAssertion(() => Assert.Contains("Nuovo progetto", page.Markup));

        page.Find("select[aria-label='Macchina per il nuovo progetto']").Change(_equipmentId.ToString());
        page.Find("input[placeholder='es. Sostituzione forno con pompa di calore']").Change("Sostituzione forno");
        page.FindAll("button").Single(b => b.TextContent == "Crea progetto").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath == "/api/energy/projects"));
        var sent = _server.Requests.Single(r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath == "/api/energy/projects");
        Assert.Contains(_equipmentId.ToString(), sent.Body);
    }

    [Fact]
    public async Task EquipmentConsumptionLookup_PicksTheMachineFromTheDropdown_AndSendsItsRealId()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", "/api/equipment", new List<Equipment> { new(_equipmentId, "Forno 1", "FR-01", null, null, true) });
        _server.OnJson("GET", "/api/energy/projects", new List<EnergyProject>());

        var page = RenderComponent<Energy>();
        page.WaitForAssertion(() => Assert.Contains("Consumo di una macchina", page.Markup));

        page.Find("select[aria-label='Macchina per il consumo']").Change(_equipmentId.ToString());
        _server.OnJson("GET", $"/api/energy/equipment/{_equipmentId}/consumption?from={DateTime.Today.AddDays(-30):yyyy-MM-dd}&to={DateTime.Today:yyyy-MM-dd}",
            new EnergyConsumption(42m, 3, DateTime.Today.AddDays(-30), DateTime.Today));
        page.FindAll("button").First(b => b.TextContent == "Calcola consumo").Click();

        page.WaitForAssertion(() => Assert.Contains("42", page.Markup));
    }
}
