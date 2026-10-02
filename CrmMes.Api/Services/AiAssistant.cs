using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace CrmMes.Api.Services;

public sealed record AiAssistantResponse(string Answer, string Provider);

public interface IAiAssistant
{
    string ProviderKey { get; }
    Task<AiAssistantResponse> AskAsync(string question, CancellationToken cancellationToken = default);
}

/// <summary>Offline-friendly answers about Nicolò MES modules (development and demos).</summary>
public sealed class StubAiAssistant : IAiAssistant
{
    public string ProviderKey => "stub";

    public Task<AiAssistantResponse> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var text = (question ?? string.Empty).Trim();
        var lower = text.ToLowerInvariant();
        string answer;

        if (string.IsNullOrWhiteSpace(text))
        {
            answer = "Scrivi una domanda su fatturazione, OEE, MRP o magazzino: ti indico dove trovare la funzione nel gestionale.";
        }
        else if (lower.Contains("fattur") || lower.Contains("sdi") || lower.Contains("xml"))
        {
            answer =
                "Fatturazione elettronica: da Vendite → Fatture crei la fattura differita dai DDT, controlli i totali e scarichi l'XML FatturaPA " +
                "o invii allo SDI se configurato. I dati fiscali dell'azienda e del cliente stanno in anagrafica.";
        }
        else if (lower.Contains("oee") || lower.Contains("efficien") || lower.Contains("fermo"))
        {
            answer =
                "OEE e produzione: sul cruscotto e sulle commesse vedi avanzamento, tempi e fermi macchina. " +
                "Le operazioni sui centri di lavoro alimentano tempi standard vs consuntivi per calcolare performance.";
        }
        else if (lower.Contains("mrp") || lower.Contains("fabbisogn") || lower.Contains("ordini fornitore"))
        {
            answer =
                "MRP e acquisti: in Acquisti → Fabbisogni MRP il sistema propone cosa ordinare in base a commesse e scorte. " +
                "Da lì puoi generare ordini fornitore e seguire le consegne.";
        }
        else if (lower.Contains("magazz") || lower.Contains("stock") || lower.Contains("lott") || lower.Contains("ubicaz"))
        {
            answer =
                "Magazzino: Materiali, lotti, distinte di prelievo e ubicazioni gestiscono giacenze e tracciabilità. " +
                "I prelievi per commessa scaricano i lotti collegati all'ordine di lavoro.";
        }
        else
        {
            answer =
                "Sono l'assistente di Nicolò MES. Posso orientarti su fatture e SDI, OEE/produzione, MRP/acquisti e magazzino. " +
                "Riformula la domanda con una di queste parole chiave.";
        }

        return Task.FromResult(new AiAssistantResponse(answer, ProviderKey));
    }
}

public sealed class HttpAiAssistant : IAiAssistant
{
    public const string HttpClientName = "AiAssistant";
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public HttpAiAssistant(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _http = httpClientFactory.CreateClient(HttpClientName);
        _configuration = configuration;
    }

    public string ProviderKey => "openai";

    public async Task<AiAssistantResponse> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Ai:ApiKey"] ?? Environment.GetEnvironmentVariable("CRM_MES_AI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Ai:ApiKey non configurata.");
        }

        var model = _configuration["Ai:Model"]?.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            model = "gpt-4o-mini";
        }

        var baseUrl = _configuration["Ai:BaseUrl"]?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = "https://api.openai.com/v1";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new ChatCompletionRequest(
            model,
            [
                new ChatMessage("system", "Sei l'assistente di Nicolò MES, un gestionale MES/ERP italiano. Rispondi in italiano, conciso e pratico."),
                new ChatMessage("user", question ?? string.Empty),
            ]));

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Risposta AI non valida.");
        var answer = payload.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
        if (string.IsNullOrWhiteSpace(answer))
        {
            throw new InvalidOperationException("Risposta AI vuota.");
        }

        return new AiAssistantResponse(answer, ProviderKey);
    }

    private sealed record ChatCompletionRequest(string Model, List<ChatMessage> Messages);
    private sealed record ChatMessage(string Role, string Content);
    private sealed record ChatCompletionResponse(List<ChatChoice>? Choices);
    private sealed record ChatChoice(ChatMessageOut? Message);
    private sealed record ChatMessageOut(string? Content);
}
