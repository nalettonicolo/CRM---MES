using Bunit;
using CrmMes.Web.Pages;
using CrmMes.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CrmMes.Web.Tests;

public class UsersPageTests : TestContext
{
    private readonly FakeServer _server = new();
    private readonly Guid _userId = Guid.NewGuid();

    public UsersPageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_server.CreateClient());
        Services.AddScoped<Session>();
        Services.AddScoped<Api>();
        _server.OnJson("GET", "/api/users", new List<UserRow> { new(_userId, "Mario Rossi", "mario@test.local", "Operator", true, DateTime.UtcNow) });
    }

    private async Task<IRenderedComponent<Users>> OpenAsync()
    {
        await Services.GetRequiredService<Session>().SetAsync(
            new AuthResponse("t", "r", DateTime.UtcNow.AddMinutes(30), Guid.NewGuid(), "Prova", "prova@example.test", "Admin"));
        var page = RenderComponent<Users>();
        page.WaitForAssertion(() => Assert.Contains("Mario Rossi", page.Markup));
        return page;
    }

    [Fact]
    public async Task Anonymize_AsksForConfirmation_ThenCallsTheEndpoint()
    {
        _server.OnJson("POST", $"/api/users/{_userId}/anonymize", new { });
        var page = await OpenAsync();

        page.Find("button[class*='secondary small']").Click();
        page.WaitForAssertion(() => Assert.Contains("Anonimizzare Mario Rossi?", page.Markup));

        page.FindAll("button").Single(b => b.TextContent == "Anonimizza").Click();

        page.WaitForAssertion(() => Assert.Contains(_server.Requests, r => r.Request.Method == HttpMethod.Post
            && r.Request.RequestUri!.PathAndQuery == $"/api/users/{_userId}/anonymize"));
    }

    [Fact]
    public async Task Cancel_DoesNotCallTheEndpoint()
    {
        var page = await OpenAsync();

        page.Find("button[class*='secondary small']").Click();
        page.WaitForAssertion(() => Assert.Contains("Anonimizzare Mario Rossi?", page.Markup));
        page.FindAll("button").Single(b => b.TextContent == "Annulla").Click();

        Assert.DoesNotContain("Anonimizzare Mario Rossi?", page.Markup);
        Assert.DoesNotContain(_server.Requests, r => r.Request.Method == HttpMethod.Post);
    }
}
