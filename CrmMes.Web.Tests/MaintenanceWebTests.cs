using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class MaintenanceWebTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _equipmentId = Guid.NewGuid();

    public MaintenanceWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private Task LogInAsync(string role) => Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    private MaintenanceTask Task(string status, DateTime? dueDate, int? recurrenceDays = null) => new(
        Guid.NewGuid(), _equipmentId, "Pressa 120t", "Controllo cinghie", null, "Preventiva", status, dueDate, status == "Completed" ? DateTime.UtcNow : null, recurrenceDays, null);

    [Fact]
    public async Task OverdueTask_IsFlagged_AndOperatorCannotCompleteOrCreate()
    {
        await LogInAsync("Operator");
        _server.OnJson("GET", "/api/equipment", new List<Equipment> { new(_equipmentId, "Pressa 120t", "PR-01", null, null, true) });
        _server.OnJson("GET", "/api/maintenance-tasks?status=Pending", new List<MaintenanceTask> { Task("Pending", DateTime.Today.AddDays(-3)) });

        var page = RenderComponent<Maintenance>();

        page.WaitForAssertion(() => Assert.Contains("Controllo cinghie", page.Markup));
        Assert.Contains("in ritardo", page.Markup);
        Assert.Empty(page.FindAll("button"));
        Assert.DoesNotContain("Nuovo intervento", page.Markup);
    }

    [Fact]
    public async Task CompletingARecurringTask_RefreshesTheList_AndSaysTheNextOneWasGenerated()
    {
        await LogInAsync("Warehouse");
        var task = Task("Pending", DateTime.Today.AddDays(10), recurrenceDays: 30);
        _server.OnJson("GET", "/api/equipment", new List<Equipment> { new(_equipmentId, "Pressa 120t", "PR-01", null, null, true) });
        _server.OnJson("GET", "/api/maintenance-tasks?status=Pending", new List<MaintenanceTask> { task });
        _server.OnJson("POST", $"/api/maintenance-tasks/{task.Id}/complete", task with { Status = "Completed" });

        var page = RenderComponent<Maintenance>();
        page.WaitForAssertion(() => Assert.Contains("Completa", page.Markup));

        page.Find("button.secondary.small").Click();

        page.WaitForAssertion(() => Assert.Contains("generata la prossima scadenza", page.Markup));
        Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post && r.Request.RequestUri!.AbsolutePath.EndsWith("/complete"));
    }
}
