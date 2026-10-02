using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace CrmMes.Api.Services;

public interface ISdiProvider
{
    bool IsConfigured { get; }

    Task<SdiProviderResult> SubmitAsync(string fileName, string xml, CancellationToken cancellationToken);
}

public sealed record SdiProviderResult(bool Submitted, string? TransmissionId, string? Message);

public sealed class StubSdiProvider : ISdiProvider
{
    public bool IsConfigured => true;

    public Task<SdiProviderResult> SubmitAsync(string fileName, string xml, CancellationToken cancellationToken)
        => Task.FromResult(new SdiProviderResult(true, $"STUB-{Guid.NewGuid():N}", "Invio simulato (stub SdI)."));
}

public sealed class UnconfiguredSdiProvider : ISdiProvider
{
    public bool IsConfigured => false;

    public Task<SdiProviderResult> SubmitAsync(string fileName, string xml, CancellationToken cancellationToken)
        => Task.FromResult(new SdiProviderResult(false, null, "Nessun intermediario SdI è configurato."));
}

/// <summary>Invia fatture elettroniche a un intermediario tramite API HTTP (multipart o JSON).</summary>
public sealed class HttpSdiProvider : ISdiProvider
{
    public const string HttpClientName = nameof(HttpSdiProvider);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public HttpSdiProvider(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_configuration["Sdi:BaseUrl"]);

    public async Task<SdiProviderResult> SubmitAsync(string fileName, string xml, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return new SdiProviderResult(false, null, "Intermediario SdI HTTP non configurato (manca Sdi:BaseUrl).");
        }

        var baseUrl = _configuration["Sdi:BaseUrl"]!.Trim().TrimEnd('/');
        var submitPath = _configuration["Sdi:SubmitPath"]?.Trim();
        if (string.IsNullOrWhiteSpace(submitPath))
        {
            submitPath = "/submit";
        }

        if (!submitPath.StartsWith('/'))
        {
            submitPath = "/" + submitPath;
        }

        var requestUri = baseUrl + submitPath;
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        var apiKey = _configuration["Sdi:ApiKey"]?.Trim();
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        request.Content = JsonContent.Create(new { fileName, xml });

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken);
                return new SdiProviderResult(false, null,
                    $"Intermediario SdI ha risposto {(int)response.StatusCode}: {TrimForMessage(body)}");
            }

            var transmissionFromHeader = FirstHeader(response, "X-Transmission-Id", "Transmission-Id", "X-Request-Id");
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var payload = await response.Content.ReadFromJsonAsync<SdiSubmitResponse>(JsonOptions, cancellationToken);
                if (payload is not null)
                {
                    if (payload.Success == false)
                    {
                        return new SdiProviderResult(false, payload.TransmissionId,
                            payload.Message ?? "Intermediario SdI ha rifiutato l'invio.");
                    }

                    var id = payload.TransmissionId ?? transmissionFromHeader ?? NewTransmissionId();
                    return new SdiProviderResult(true, id, payload.Message ?? "Fattura inviata all'intermediario SdI.");
                }
            }

            var idFallback = transmissionFromHeader ?? NewTransmissionId();
            return new SdiProviderResult(true, idFallback, "Fattura inviata all'intermediario SdI.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SdiProviderResult(false, null, "Timeout durante l'invio all'intermediario SdI (30 s).");
        }
        catch (HttpRequestException ex)
        {
            return new SdiProviderResult(false, null, $"Impossibile contattare l'intermediario SdI: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new SdiProviderResult(false, null, $"Errore imprevisto durante l'invio SdI: {ex.Message}");
        }
    }

    private static string? FirstHeader(HttpResponseMessage response, params string[] names)
    {
        foreach (var name in names)
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                var value = values.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    private static async Task<string?> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private static string TrimForMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "nessun dettaglio.";
        }

        var trimmed = body.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200] + "…";
    }

    private static string NewTransmissionId() => $"HTTP-{Guid.NewGuid():N}";

    private sealed class SdiSubmitResponse
    {
        public bool? Success { get; set; }
        public string? TransmissionId { get; set; }
        public string? Message { get; set; }
    }
}
