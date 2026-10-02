using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace CrmMes.Desktop;

/// <summary>Checks GitHub Releases for a newer client and updates through the suite installer
/// (installer/NicoloMES.iss → NicoloMES-Setup.exe). With /UPDATE=1 the suite installs only the
/// desktop program and never touches a server installation.
///
/// Until v1.2 the update unzipped the release over the running folder from a .cmd script that waited a
/// fixed 2 seconds: the app often hadn't finished closing (its logout call can take much longer while the
/// free Render instance wakes up), files were still locked, Expand-Archive failed in a console window
/// that closed by itself, and nothing restarted the program. Now the installer does it: it closes the app
/// through Windows Restart Manager and waits until it has really exited, shows real errors, and reopens
/// the program when done.</summary>
public sealed class UpdateService
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/nalettonicolo/CRM---MES/releases/latest";
    internal const string SetupAssetName = "NicoloMES-Setup.exe";
    internal const string ChecksumAssetName = SetupAssetName + ".sha256";
    private readonly HttpClient _httpClient = new();

    public UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrmMes-Desktop-Updater/2.0");
    }

    public Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    /// <summary>Development builds carry the placeholder version 0.x from the .csproj: they would always
    /// "find" an update, so the automatic check at login skips them.</summary>
    public bool IsDevelopmentBuild => CurrentVersion.Major == 0;

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(ReleasesEndpoint, cancellationToken);
        if (release is null || release.Draft || release.Prerelease)
        {
            return null;
        }

        return IsNewer(release.TagName, CurrentVersion, out var version)
            ? new ReleaseInfo(
                version, release.TagName, release.Name, release.Body,
                release.Assets.FirstOrDefault(asset => asset.Name == SetupAssetName)?.BrowserDownloadUrl,
                release.Assets.FirstOrDefault(asset => asset.Name == ChecksumAssetName)?.BrowserDownloadUrl)
            : null;
    }

    /// <summary>"v1.2.0" vs the running assembly version, comparing major.minor.build only (the assembly's
    /// 4th component is always 0 and must not make 1.2.0 look newer than 1.2.0.0).</summary>
    internal static bool IsNewer(string tagName, Version current, out Version version)
    {
        if (!Version.TryParse(tagName.TrimStart('v', 'V'), out var parsed))
        {
            version = new Version(0, 0);
            return false;
        }

        version = parsed;
        var normalizedCurrent = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        var normalizedRelease = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        return normalizedRelease > normalizedCurrent;
    }

    /// <summary>Downloads the installer, verifies its SHA-256 against the checksum published by the release
    /// pipeline, and starts it in update mode. The caller then closes the application; the installer waits
    /// for that, updates and reopens the program.</summary>
    public async Task StartUpdateAsync(ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            throw new InvalidOperationException(
                $"La release {release.TagName} non contiene l'installer {SetupAssetName}. Scaricalo a mano dalla pagina delle release su GitHub.");
        }

        if (string.IsNullOrWhiteSpace(release.ChecksumUrl))
        {
            throw new InvalidOperationException(
                $"La release {release.TagName} non pubblica il checksum {ChecksumAssetName}: impossibile verificarne l'integrità, aggiornamento annullato.");
        }

        var setupPath = Path.Combine(Path.GetTempPath(), $"NicoloMES-Setup-{release.Version}.exe");
        await using (var input = await _httpClient.GetStreamAsync(release.DownloadUrl, cancellationToken))
        await using (var output = File.Create(setupPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        // Until the installer is code-signed, the published SHA-256 is the only integrity check: a corrupted
        // download or a replaced asset (without also replacing the checksum) is refused, never executed.
        var expectedHash = (await _httpClient.GetStringAsync(release.ChecksumUrl, cancellationToken)).Trim();
        string actualHash;
        await using (var setupStream = File.OpenRead(setupPath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(setupStream, cancellationToken));
        }

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(setupPath);
            throw new InvalidOperationException(
                $"L'installer scaricato per {release.TagName} non corrisponde al checksum pubblicato: potrebbe essere corrotto o alterato. Aggiornamento annullato.");
        }

        var logPath = Path.Combine(Path.GetTempPath(), "NicoloMES-update.log");
        Process.Start(new ProcessStartInfo
        {
            FileName = setupPath,
            // /SILENT: progress window only, no questions; real errors are still shown.
            // /CLOSEAPPLICATIONS: waits for this program to close (Restart Manager) before replacing files.
            // /UPDATE=1: the installer reopens the program when done (see [Run] in NicoloMES.iss).
            Arguments = $"/SILENT /SP- /NOCANCEL /CLOSEAPPLICATIONS /TYPE=client /COMPONENTS=\"client\" /UPDATE=1 /LOG=\"{logPath}\"",
            UseShellExecute = true
        });
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;
        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }
        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}

public sealed record ReleaseInfo(Version Version, string TagName, string Name, string Notes, string? DownloadUrl, string? ChecksumUrl);
