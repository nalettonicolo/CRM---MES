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

    public static ClientSettings Load() => Load(SettingsPath);

    /// <summary>Test seam: lets CrmMes.Desktop.Tests exercise the load/parse/fallback logic against a
    /// temp file instead of the real user profile, so a test run can never clobber someone's actual
    /// saved server address.</summary>
    internal static ClientSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
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

    public void Save() => Save(SettingsPath);

    /// <summary>Test seam, see <see cref="Load(string)"/>.</summary>
    internal void Save(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
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
