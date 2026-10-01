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
}
