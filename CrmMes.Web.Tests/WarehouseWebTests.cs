using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class WarehouseWebTests : TestContext
{
    private readonly FakeServer _server = new();

    public WarehouseWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
    }

    private async Task LogInAsync()
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", "Admin"));
    }

    [Fact]
    public async Task WithdrawalSlips_ShowStatusAndMissingCounts()
    {
        await LogInAsync();
        _server.OnJson("GET", "/api/withdrawal-slips", new[]
        {
            new WithdrawalSlipSummary(Guid.NewGuid(), "DP-2026-01", Guid.NewGuid(), Guid.NewGuid(), "Ready", DateTime.UtcNow, 4, 1, Guid.NewGuid(), "WO-9"),
        });

        var page = RenderComponent<WithdrawalSlips>();
        page.WaitForAssertion(() => Assert.Contains("DP-2026-01", page.Markup));
        Assert.Contains("Pronta", page.Markup);
        Assert.Contains("WO-9", page.Markup);
        Assert.Contains("distinte/", page.Markup);
        Assert.Contains(">1<", page.Markup);
    }

    [Fact]
    public async Task MaterialLots_ShowResidualQuantity()
    {
        await LogInAsync();
        _server.OnJson("GET", "/api/material-lots?onlyWithStock=false", new[]
        {
            new MaterialLotSummary(Guid.NewGuid(), "CAV-FG16", "L-7781", 12.5m, 20, null, null, DateTime.UtcNow),
        });

        var page = RenderComponent<MaterialLots>();
        page.WaitForAssertion(() => Assert.Contains("L-7781", page.Markup));
        Assert.Contains("CAV-FG16", page.Markup);
        Assert.Contains("12,5", page.Markup);
    }

    [Fact]
    public async Task MaterialLots_OnlyWithStock_RequestsTheFilter()
    {
        await LogInAsync();
        _server.OnJson("GET", "/api/material-lots?onlyWithStock=false", Array.Empty<MaterialLotSummary>());
        _server.OnJson("GET", "/api/material-lots?onlyWithStock=true", new[]
        {
            new MaterialLotSummary(Guid.NewGuid(), "CAV-FG16", "L-STOCK", 1, 1, null, null, DateTime.UtcNow),
        });

        var page = RenderComponent<MaterialLots>();
        page.WaitForAssertion(() => Assert.Contains("Nessun lotto", page.Markup));
        page.Find("input[type=checkbox]").Change(true);
        page.WaitForAssertion(() => Assert.Contains("L-STOCK", page.Markup));
    }

    [Fact]
    public async Task CatalogSearch_ListsPartNumbers()
    {
        await LogInAsync();
        Services.GetRequiredService<Session>().Company = new CompanyProfile(
            true, "Quadri", null, null, null, null, "electrical-panels", ["purchasing"]);
        _server.OnJson("GET", "/api/supplier-catalog/search?q=1SDA", new[]
        {
            new CatalogSearchResult("1SDA", "Interruttore", Guid.NewGuid(), "ABB", "ABB", null, "1SDA0678", null, 12.5m, 7),
        });

        var page = RenderComponent<CatalogSearch>();
        page.Find("input[type=search]").Input("1SDA");
        page.WaitForAssertion(() => Assert.Contains("1SDA0678", page.Markup));
        Assert.DoesNotContain("Importa listino Metel", page.Markup);
        Assert.Contains("12,50", page.Markup);
    }

    [Fact]
    public async Task Quality_ListsNonConformities()
    {
        await LogInAsync();
        _server.OnJson("GET", "/api/quality-measurements/non-conformities", new[]
        {
            new QualityNonConformity(Guid.NewGuid(), Guid.NewGuid(), "C-1", "Collaudo", "Isolamento basso", 1, null, DateTime.UtcNow, "Luca", null),
        });
        _server.OnJson("GET", "/api/quality-checkpoints", Array.Empty<QualityCheckpoint>());

        var page = RenderComponent<Quality>();
        page.WaitForAssertion(() => Assert.Contains("Isolamento basso", page.Markup));
        Assert.Contains("C-1", page.Markup);
    }
}
