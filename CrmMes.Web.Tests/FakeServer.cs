using System.Net;
using System.Text;
using System.Text.Json;

namespace CrmMes.Web.Tests;

/// <summary>Answers the web app's HTTP calls in memory: one handler per "METHOD path", every request kept
/// (with its body) so tests can check what the app sent.</summary>
public sealed class FakeServer : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = [];

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

    public FakeServer On(string method, string pathAndQuery, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        _routes[$"{method} {pathAndQuery}"] = respond;
        return this;
    }

    public FakeServer OnJson(string method, string pathAndQuery, object body, HttpStatusCode status = HttpStatusCode.OK) =>
        On(method, pathAndQuery, _ => Json(body, status));

    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)), Encoding.UTF8, "application/json"),
    };

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri("https://gestionale.test/") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request, body));
        var key = $"{request.Method} {request.RequestUri!.PathAndQuery}";
        return _routes.TryGetValue(key, out var respond)
            ? respond(request)
            : new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}
