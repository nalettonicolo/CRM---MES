using System.Text.Json;
using Microsoft.JSInterop;

namespace CrmMes.Web.Services;

/// <summary>Who is logged in on this browser tab. Kept in sessionStorage, not localStorage: closing the tab
/// ends the session, and a token never outlives the tab it was issued to (a shared office PC is common).
/// Also holds what the menus are built from: the company profile and the areas the Admin shows on the web.</summary>
public sealed class Session
{
    private const string StorageKey = "nicolomes.session";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IJSRuntime _js;

    public Session(IJSRuntime js)
    {
        _js = js;
    }

    public AuthResponse? Auth { get; private set; }
    public CompanyProfile? Company { get; set; }

    /// <summary>Areas shown on the web (null: not loaded yet, or a server without channels, show all).</summary>
    public IReadOnlyList<string>? WebAreas { get; set; }

    public bool IsLoggedIn => Auth is not null;
    public string Role => Auth?.Role ?? string.Empty;
    public bool IsAdmin => Role == "Admin";

    public event Action? Changed;

    public async Task RestoreAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
            Auth = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<AuthResponse>(json, Json);
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            Auth = null;
        }
    }

    public async Task SetAsync(AuthResponse auth)
    {
        Auth = auth;
        try
        {
            await _js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, JsonSerializer.Serialize(auth, Json));
        }
        catch (JSException)
        {
            // Storage blocked (private mode in some browsers): the session lives until the page is reloaded.
        }

        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        Auth = null;
        Company = null;
        WebAreas = null;
        try
        {
            await _js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        }
        catch (JSException)
        {
        }

        Changed?.Invoke();
    }

    public void NotifyChanged() => Changed?.Invoke();
}
