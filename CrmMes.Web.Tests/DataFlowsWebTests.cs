using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class DataFlowsPageTests : TestContext
{
    private readonly FakeServer _server = new();

    public DataFlowsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/api/data-flows/events", new List<DataFlowEvent>
        {
            new(DataFlowEventsKeys.EngineeringChangeApplied, "Modifica tecnica applicata (ufficio tecnico)",
                [new("changeNumber", "Numero della modifica"), new("productCode", "Codice prodotto")]),
        });
        _server.OnJson("GET", "/api/company-profile/access", new AccessChannels(
            ["Web"], new Dictionary<string, List<string>>(), new Dictionary<string, List<string>>(), [],
            ["Admin", "Sales", "Purchasing", "Warehouse", "Operator", "Management"]));
        _server.OnJson("GET", "/api/data-flows", new List<DataFlow>());
    }

    private async Task LogInAsync(string role) => await Services.GetRequiredService<Session>().SetAsync(
        new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", role));

    [Fact]
    public async Task NonAdmin_SeesReadOnlyMessage()
    {
        await LogInAsync("Operator");
        var page = RenderComponent<DataFlows>();

        Assert.Contains("Solo amministratore", page.Markup);
        Assert.Empty(_server.Requests);
    }

    [Fact]
    public async Task Admin_CreatesAFlow_WithOneNotifyStep()
    {
        await LogInAsync("Admin");
        _server.OnJson("POST", "/api/data-flows", new DataFlow(Guid.NewGuid(), "Avviso magazzino", DataFlowEventsKeys.EngineeringChangeApplied, true,
            [new(Guid.NewGuid(), 0, DataFlowStepTypes.NotifyRole, "Warehouse", "Modifica {{changeNumber}} su {{productCode}}.")]));
        var page = RenderComponent<DataFlows>();
        page.WaitForAssertion(() => Assert.Contains("Nuovo flusso", page.Markup));

        page.Find("input[aria-label='Nome del flusso']").Change("Avviso magazzino");
        page.Find("select[aria-label='Ruolo del passo']").Change("Warehouse");
        page.Find("input[aria-label='Messaggio del passo']").Change("Modifica {{changeNumber}} su {{productCode}}.");
        page.FindAll("button").Single(b => b.TextContent == "Crea flusso").Click();

        page.WaitForAssertion(() => Assert.Contains("Flusso salvato.", page.Markup));
        Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post
            && r.Request.RequestUri!.PathAndQuery == "/api/data-flows"
            && r.Body.Contains("Avviso magazzino")
            && r.Body.Contains("Warehouse"));
    }
}

public class NotificationsPageTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _notificationId = Guid.NewGuid();
    private readonly Guid _runId = Guid.NewGuid();

    public NotificationsPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/api/notifications", new List<Notification>
        {
            new(_notificationId, "Modifica 7 su MAC-1 applicata.", null, DateTime.UtcNow, null),
        });
        _server.OnJson("GET", "/api/data-flows/runs?status=WaitingApproval", new List<DataFlowRun>
        {
            new(_runId, "Avviso acquisti", DataFlowEventsKeys.EngineeringChangeApplied, "WaitingApproval", 1, "Purchasing", null, DateTime.UtcNow, DateTime.UtcNow),
        });
    }

    private async Task<IRenderedComponent<Notifications>> OpenAsync()
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", "Purchasing"));
        var page = RenderComponent<Notifications>();
        page.WaitForAssertion(() => Assert.Contains("Modifica 7 su MAC-1 applicata.", page.Markup));
        return page;
    }

    [Fact]
    public async Task ShowsNotifications_AndPendingApprovals()
    {
        var page = await OpenAsync();

        Assert.Contains("Avviso acquisti", page.Markup);
        Assert.Contains("Approva", page.Markup);
    }

    [Fact]
    public async Task Approving_CallsTheRightEndpoint()
    {
        _server.OnJson("POST", $"/api/data-flows/runs/{_runId}/approve",
            new DataFlowRun(_runId, "Avviso acquisti", DataFlowEventsKeys.EngineeringChangeApplied, "Completed", 2, null, "Prova", DateTime.UtcNow, DateTime.UtcNow));
        var page = await OpenAsync();

        page.FindAll("button").Single(b => b.TextContent == "Approva").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post
            && r.Request.RequestUri!.PathAndQuery == $"/api/data-flows/runs/{_runId}/approve"));
    }

    [Fact]
    public async Task MarkingRead_CallsTheRightEndpoint()
    {
        _server.OnJson("POST", $"/api/notifications/{_notificationId}/read", new { });
        var page = await OpenAsync();

        page.FindAll("button").Single(b => b.TextContent == "Segna come letta").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post
            && r.Request.RequestUri!.PathAndQuery == $"/api/notifications/{_notificationId}/read"));
    }
}

/// <summary>Stessa chiave dell'evento cablato in CrmMes.Core.Flows.DataFlowEvents, duplicata qui perché i test
/// web non referenziano CrmMes.Core.</summary>
internal static class DataFlowEventsKeys
{
    public const string EngineeringChangeApplied = "engineering.change.applied";
}
