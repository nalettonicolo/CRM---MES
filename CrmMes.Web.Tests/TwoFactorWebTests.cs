using Bunit;
using CrmMes.Web.Layout;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class TwoFactorWebTests : TestContext
{
    private readonly FakeServer _server = new();

    public TwoFactorWebTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/ping", new { status = "ok" });
    }

    [Fact]
    public void Login_AsksForTheCode_ThenOpensTheSession()
    {
        var id = Guid.NewGuid();
        _server.OnJson("POST", "/api/auth/login", new AuthResponse("", "", DateTime.UtcNow.AddMinutes(5), id, "Anna", "anna@example.test", "Admin", "sfida-123"));
        _server.OnJson("POST", "/api/auth/login/2fa", new AuthResponse("token", "refresh", DateTime.UtcNow.AddMinutes(30), id, "Anna", "anna@example.test", "Admin"));
        var login = RenderComponent<Login>();

        login.Find("input[type=email]").Change("anna@example.test");
        login.Find("input[type=password]").Change("prova");
        login.Find("form").Submit();

        login.WaitForAssertion(() => Assert.Contains("codice di 6 cifre", login.Markup));
        var session = Services.GetRequiredService<Session>();
        Assert.False(session.IsLoggedIn);

        login.Find("input[autocomplete=one-time-code]").Change("123456");
        login.Find("form").Submit();

        login.WaitForAssertion(() => Assert.True(session.IsLoggedIn));
        Assert.Contains("\"challenge\":\"sfida-123\"", _server.Requests.Last().Body);
        Assert.Equal("token", session.Auth!.Token);
    }
}
