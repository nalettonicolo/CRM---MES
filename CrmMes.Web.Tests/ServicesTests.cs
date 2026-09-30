using System.Net;
using Bunit;
using CrmMes.Web.Services;

namespace CrmMes.Web.Tests;

public class LabelsTests
{
    [Theory]
    [InlineData("Draft", "Bozza", "")]
    [InlineData("Released", "Rilasciata", "info")]
    [InlineData("InProgress", "In lavorazione", "warn")]
    [InlineData("Completed", "Completata", "ok")]
    [InlineData("Cancelled", "Annullata", "danger")]
    public void WorkOrderStatus_HasItalianNameAndColour(string status, string name, string css)
    {
        Assert.Equal(name, Labels.WorkOrderStatus(status));
        Assert.Equal(css, Labels.WorkOrderStatusClass(status));
    }

    [Theory]
    [InlineData(0, 4, "w0")]
    [InlineData(1, 4, "w30")]
    [InlineData(2, 4, "w50")]
    [InlineData(4, 4, "w100")]
    [InlineData(5, 4, "w100")]
    [InlineData(1, 0, "w0")]
    public void ProgressClass_RoundsToStepsOfTen(int done, int total, string expected) =>
        Assert.Equal(expected, Labels.ProgressClass(done, total));

    [Fact]
    public void Numbers_AndDates_AreItalian()
    {
        Assert.Equal("1.234,50", Labels.Number(1234.5m));
        Assert.Equal("12", Labels.Number(12m));
        Assert.Equal("85%", Labels.Percent(0.85m));
        Assert.Equal("—", Labels.Date(null));
    }

    [Fact]
    public void Overdue_OnlyForUnfinishedOrdersPastTheirDate()
    {
        var now = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(Labels.IsOverdue(now.AddDays(-2), "InProgress", now));
        Assert.False(Labels.IsOverdue(now.AddDays(-2), "Completed", now));
        Assert.False(Labels.IsOverdue(now.AddDays(1), "Released", now));
        Assert.False(Labels.IsOverdue(null, "Released", now));
    }
}

public class NavigationTests
{
    [Fact]
    public void Menu_ShowsOnlyAreasTheAdminKeepsOnTheWeb()
    {
        var entries = Navigation.Visible(["production", "invoicing"], "Sales");

        Assert.Equal(["Commesse"], entries.Select(e => e.Title));
        Assert.Equal(["invoicing"], Navigation.DesktopOnly(["production", "invoicing"]));
    }

    [Fact]
    public void WithoutChannelSettings_EveryWebPageIsShown() =>
        Assert.Equal(Navigation.WebPages.Count, Navigation.Visible(null, "Operator").Count);

    [Fact]
    public void Restrictions_StoreOnlyWhatDiffersFromEverything()
    {
        var restrictions = AccessRestrictions.From(new()
        {
            ["Sales"] = ["web"],
            ["Operator"] = ["desktop", "web", "mobile"],
        }, ["desktop", "web", "mobile"]);

        Assert.Equal(["web"], restrictions["Sales"]);
        Assert.False(restrictions.ContainsKey("Operator"));
    }
}

public class ApiTests : TestContext
{
    private static readonly AuthResponse Auth = new("access-1", "refresh-1", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Anna Rossi", "anna@example.test", "Sales");

    public ApiTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private (Api Api, Session Session) Create(FakeServer server)
    {
        var session = new Session(JSInterop.JSRuntime);
        return (new Api(server.CreateClient(), session), session);
    }

    [Theory]
    [InlineData("{\"message\":\"Il tuo ruolo non può accedere\"}", "Il tuo ruolo non può accedere")]
    [InlineData("{\"title\":\"Errore\",\"detail\":\"Dettaglio\"}", "Dettaglio")]
    [InlineData("<html>errore</html>", null)]
    [InlineData("", null)]
    public void ErrorMessages_AreTakenFromTheApiAnswer(string body, string? expected) =>
        Assert.Equal(expected, Api.ExtractMessage(body));

    [Fact]
    public async Task Login_DeclaresTheWebChannel_AndKeepsTheSession()
    {
        var server = new FakeServer().OnJson("POST", "/api/auth/login", Auth);
        var (api, session) = Create(server);

        await api.LoginAsync("anna@example.test", "segreta-di-prova");

        Assert.Contains("\"channel\":\"web\"", server.Requests.Single().Body);
        Assert.Equal("Anna Rossi", session.Auth!.Name);
    }

    [Fact]
    public async Task Login_ShowsTheServerReason_WhenTheRoleCannotUseTheWeb()
    {
        var server = new FakeServer().OnJson("POST", "/api/auth/login",
            new { message = "Il tuo ruolo non può accedere dalla piattaforma web. Chiedi all'amministratore." }, HttpStatusCode.Forbidden);
        var (api, session) = Create(server);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.LoginAsync("anna@example.test", "x"));

        Assert.Contains("piattaforma web", error.Message);
        Assert.False(session.IsLoggedIn);
    }

    [Fact]
    public async Task ExpiredAccessToken_IsRenewedOnce_AndTheRequestRepeated()
    {
        var renewed = Auth with { Token = "access-2", RefreshToken = "refresh-2" };
        var server = new FakeServer()
            .On("GET", "/api/materials", request => request.Headers.Authorization!.Parameter == "access-2"
                ? FakeServer.Json(Array.Empty<Material>())
                : new HttpResponseMessage(HttpStatusCode.Unauthorized))
            .OnJson("POST", "/api/auth/refresh", renewed);
        var (api, session) = Create(server);
        await session.SetAsync(Auth);

        var materials = await api.GetAsync<List<Material>>("api/materials");

        Assert.Empty(materials);
        Assert.Equal("access-2", session.Auth!.Token);
        Assert.Equal(3, server.Requests.Count);
    }

    [Fact]
    public async Task RefusedRenewal_EndsTheSession()
    {
        var server = new FakeServer()
            .On("GET", "/api/materials", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized))
            .OnJson("POST", "/api/auth/refresh", new { message = "Il tuo ruolo non può più accedere dalla piattaforma web." }, HttpStatusCode.Forbidden);
        var (api, session) = Create(server);
        await session.SetAsync(Auth);

        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetAsync<List<Material>>("api/materials"));

        Assert.Equal(HttpStatusCode.Unauthorized, error.Status);
        Assert.False(session.IsLoggedIn);
    }
}
