using System.IO;
using System.Text.Json;

namespace CrmMes.Desktop;

/// <summary>
/// Impostazioni del client persistite in %LOCALAPPDATA%\CrmMes\settings.json, così l'indirizzo
/// dell'API può essere cambiato (es. da localhost a un server pubblicato) senza ricompilare.
/// </summary>
public sealed class ClientSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CrmMes",
        "settings.json");

    public const string DefaultApiBaseUrl = "http://localhost:5092/";

    public string ApiBaseUrl { get; set; } = DefaultApiBaseUrl;

    public static ClientSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<ClientSettings>(json);
                if (settings is not null && !string.IsNullOrWhiteSpace(settings.ApiBaseUrl))
                {
                    return settings;
                }
            }
        }
        catch
        {
            // File corrotto o non leggibile: si riparte dalle impostazioni di default.
        }

        return new ClientSettings();
    }

    public void Save()
    {
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public bool IsLocalHost()
    {
        if (!Uri.TryCreate(ApiBaseUrl, UriKind.Absolute, out var uri))
        {
            return true;
        }

        return uri.IsLoopback;
    }
}
