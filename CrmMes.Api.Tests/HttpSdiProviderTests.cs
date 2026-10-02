using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CrmMes.Api.Services;
using Microsoft.Extensions.Configuration;

namespace CrmMes.Api.Tests;

public class HttpSdiProviderTests
{
    [Fact]
    public void IsConfigured_WhenBaseUrlMissing_IsFalse()
    {
        var provider = CreateProvider(new FakeSdiHttpHandler(), new Dictionary<string, string?> { ["Sdi:BaseUrl"] = "" });
        Assert.False(provider.IsConfigured);
    }

    [Fact]
    public async Task Submit_WhenIntermediaryReturnsJson200_SucceedsWithTransmissionId()
    {
        var handler = new FakeSdiHttpHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { success = true, transmissionId = "TX-99", message = "Ricevuta." })
            });
        var provider = CreateProvider(handler, new Dictionary<string, string?>
        {
            ["Sdi:BaseUrl"] = "https://intermediario.test",
            ["Sdi:ApiKey"] = "chiave-segreta",
            ["Sdi:SubmitPath"] = "/submit",
        });

        var result = await provider.SubmitAsync("IT01234567890_00001.xml", "<FatturaElettronica/>", default);

        Assert.True(result.Submitted);
        Assert.Equal("TX-99", result.TransmissionId);
        Assert.Equal("Ricevuta.", result.Message);
        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("chiave-segreta", handler.LastRequest.Headers.Authorization!.Parameter);
        Assert.Equal("IT01234567890_00001.xml", handler.LastFileName);
    }

    [Fact]
    public async Task Submit_WhenIntermediaryReturns200WithoutJson_UsesHeaderOrGeneratedId()
    {
        var handler = new FakeSdiHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("OK") };
            response.Headers.Add("X-Transmission-Id", "HDR-42");
            return response;
        });
        var provider = CreateProvider(handler, new Dictionary<string, string?> { ["Sdi:BaseUrl"] = "https://intermediario.test" });

        var result = await provider.SubmitAsync("f.xml", "<x/>", default);

        Assert.True(result.Submitted);
        Assert.Equal("HDR-42", result.TransmissionId);
    }

    [Fact]
    public async Task Submit_WhenIntermediaryReturnsError_ReturnsItalianMessage()
    {
        var handler = new FakeSdiHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("gateway down")
        });
        var provider = CreateProvider(handler, new Dictionary<string, string?> { ["Sdi:BaseUrl"] = "https://intermediario.test" });

        var result = await provider.SubmitAsync("f.xml", "<x/>", default);

        Assert.False(result.Submitted);
        Assert.Contains("502", result.Message);
    }

    private static HttpSdiProvider CreateProvider(HttpMessageHandler handler, Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new HttpSdiProvider(new HttpClient(handler), config);
    }

    private sealed class FakeSdiHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public FakeSdiHttpHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) =>
            _respond = respond ?? (_ => new HttpResponseMessage(HttpStatusCode.OK));

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastFileName { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content is not null)
            {
                var body = await request.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                LastFileName = body.GetProperty("fileName").GetString();
            }
            if (request.RequestUri!.AbsoluteUri != "https://intermediario.test/submit")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return _respond(request);
        }
    }
}
