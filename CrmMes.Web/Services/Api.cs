using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CrmMes.Web.Services;

/// <summary>Problem shown to the user as is (the API's own message when it gives one).</summary>
public sealed class ApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>Calls to the API, which serves this app from the same address. The access token lasts 30
/// minutes: on a 401 the refresh token gets a new pair once and the request is repeated; if that fails too
/// the session ends and the login page comes back. The API decides every permission; the app only asks.</summary>
public sealed class Api
{
    public const string Channel = "web";
    private readonly HttpClient _http;
    private readonly Session _session;
    private Task<bool>? _refreshing;

    public Api(HttpClient http, Session session)
    {
        _http = http;
        _session = session;
    }

    public async Task<AuthResponse> LoginAsync(string email, string password)
    {
        using var response = await SendRawAsync(HttpMethod.Post, "api/auth/login", new { email, password, channel = Channel }, authenticated: false);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new ApiException("Email o password non corretti.", response.StatusCode);
        }

        await EnsureSuccessAsync(response);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>() ?? throw new ApiException("Risposta di accesso non valida.");
        await _session.SetAsync(auth);
        return auth;
    }

    public async Task LogoutAsync()
    {
        var refreshToken = _session.Auth?.RefreshToken;
        if (refreshToken is not null)
        {
            try
            {
                using var _ = await SendRawAsync(HttpMethod.Post, "api/auth/logout", new { refreshToken }, authenticated: true);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // Offline: the refresh token simply expires on its own.
            }
        }

        await _session.ClearAsync();
    }

    public async Task<bool> IsServerUpAsync()
    {
        try
        {
            using var response = await _http.GetAsync("ping");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task<T> GetAsync<T>(string path) => SendAsync<T>(HttpMethod.Get, path, null);

    public Task<T> PutAsync<T>(string path, object body) => SendAsync<T>(HttpMethod.Put, path, body);

    public Task<T> PostAsync<T>(string path, object? body = null) => SendAsync<T>(HttpMethod.Post, path, body);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body)
    {
        using var response = await SendAuthenticatedAsync(method, path, body);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>() ?? throw new ApiException("Risposta del server vuota.");
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(HttpMethod method, string path, object? body)
    {
        var response = await SendRawAsync(method, path, body, authenticated: true);
        if (response.StatusCode != HttpStatusCode.Unauthorized || _session.Auth is null)
        {
            return response;
        }

        response.Dispose();
        if (!await RefreshAsync())
        {
            await _session.ClearAsync();
            throw new ApiException("Sessione scaduta: accedi di nuovo.", HttpStatusCode.Unauthorized);
        }

        return await SendRawAsync(method, path, body, authenticated: true);
    }

    private Task<bool> RefreshAsync()
    {
        // Several pages may hit a 401 at once: one renewal serves them all (the refresh token rotates).
        return _refreshing ??= RefreshOnceAsync();

        async Task<bool> RefreshOnceAsync()
        {
            try
            {
                var refreshToken = _session.Auth?.RefreshToken;
                if (refreshToken is null)
                {
                    return false;
                }

                using var response = await SendRawAsync(HttpMethod.Post, "api/auth/refresh", new { refreshToken }, authenticated: false);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
                if (auth is null)
                {
                    return false;
                }

                await _session.SetAsync(auth);
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            finally
            {
                _refreshing = null;
            }
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, bool authenticated)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (authenticated && _session.Auth is { } auth)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException exception)
        {
            throw new ApiException("Server non raggiungibile. Controlla la connessione e riprova.", null) { Source = exception.Source };
        }
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var message = ExtractMessage(body);
        throw new ApiException(message ?? DefaultMessage(response.StatusCode), response.StatusCode);
    }

    internal static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Forbidden => "Il tuo ruolo non ha i permessi per questa operazione.",
        HttpStatusCode.NotFound => "Elemento non trovato: potrebbe essere stato eliminato.",
        HttpStatusCode.TooManyRequests => "Troppe richieste ravvicinate: attendi un minuto e riprova.",
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout
            => "Il server si sta avviando: riprova tra qualche secondo.",
        _ => $"Errore del server ({(int)status}).",
    };

    /// <summary>The API answers errors as {"message": "..."} or as ProblemDetails {"title", "detail"}.</summary>
    internal static string? ExtractMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var name in new[] { "message", "detail", "title" })
            {
                if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString();
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
