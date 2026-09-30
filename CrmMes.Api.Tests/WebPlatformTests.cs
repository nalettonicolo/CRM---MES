using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CrmMes.Api.Tests;

/// <summary>The web platform (CrmMes.Web) served by the API under /app/.</summary>
public class WebPlatformTests : IClassFixture<AdminSeededApiTestFixture>
{
    private readonly HttpClient _client;

    public WebPlatformTests(AdminSeededApiTestFixture fixture)
    {
        _client = fixture.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task App_IsServedUnderApp_WithItsOwnSecurityPolicy()
    {
        var redirect = await _client.GetAsync("/app");
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal("/app/", redirect.Headers.Location!.OriginalString);

        var page = await _client.GetAsync("/app/");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("<base href=\"/app/\" />", html);
        Assert.Contains("_framework/blazor.webassembly.js", html);
        Assert.DoesNotContain("<script>", html); // no inline script: the CSP would block it
        Assert.DoesNotContain("style=", html);   // no inline style either

        var csp = page.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("script-src 'self' 'wasm-unsafe-eval'", csp);
        Assert.DoesNotContain("'unsafe-eval'", csp.Replace("'wasm-unsafe-eval'", string.Empty));
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
    }

    [Fact]
    public async Task DeepLinks_OpenTheApp_WhileApiAndMissingFilesKeepTheir404()
    {
        var deepLink = await _client.GetAsync("/app/commesse/3f1f6f3c-0000-0000-0000-000000000000");
        Assert.Equal(HttpStatusCode.OK, deepLink.StatusCode);
        Assert.Contains("<base href=\"/app/\" />", await deepLink.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/app/css/manca.css")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/non-esiste")).StatusCode);
    }

    [Fact]
    public async Task StylesheetAndDownloadScript_AreServedFromTheSite()
    {
        var css = await _client.GetAsync("/app/css/app.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        Assert.Contains("prefers-color-scheme: dark", await css.Content.ReadAsStringAsync());

        var script = await _client.GetAsync("/app/js/files.js");
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Contains("saveFile", await script.Content.ReadAsStringAsync());
    }
}
