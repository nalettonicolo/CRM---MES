using System.Net.Http;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

/// <summary>Regression test for a real crash found via GUI verification: HttpClient forbids changing
/// BaseAddress after it has sent its first request, so SetBaseUrl used to throw InvalidOperationException
/// the moment a user changed the server address in Settings after the app had already made any API call
/// (which in practice is always, since login happens on startup).</summary>
public class ApiClientTests
{
    [Fact]
    public async Task SetBaseUrl_AfterARequestWasAlreadySent_DoesNotThrow()
    {
        var client = new ApiClient();
        client.SetBaseUrl("http://127.0.0.1:1/"); // unreachable: fails fast without a real server

        await Assert.ThrowsAsync<HttpRequestException>(() => client.IsHealthyAsync());

        var exception = Record.Exception(() => client.SetBaseUrl("http://127.0.0.1:2/"));

        Assert.Null(exception);
    }
}
