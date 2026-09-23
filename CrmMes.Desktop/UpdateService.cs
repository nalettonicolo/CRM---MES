using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace CrmMes.Desktop;

public sealed class UpdateService
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/nalettonicolo/CRM---MES/releases/latest";
    private const string AssetName = "CrmMes.Desktop-win-x64.zip";
    private const string ChecksumAssetName = AssetName + ".sha256";
    private readonly HttpClient _httpClient = new();

    public UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CrmMes-Desktop-Updater/1.0");
    }

    public Version CurrentVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);

    public async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(ReleasesEndpoint, cancellationToken);
        if (release is null || release.Draft || release.Prerelease)
        {
            return null;
        }

        var versionText = release.TagName.TrimStart('v', 'V');
        return Version.TryParse(versionText, out var version) && version > CurrentVersion
            ? new ReleaseInfo(
                version, release.TagName, release.Name, release.Body,
                release.Assets.FirstOrDefault(asset => asset.Name == AssetName)?.BrowserDownloadUrl,
                release.Assets.FirstOrDefault(asset => asset.Name == ChecksumAssetName)?.BrowserDownloadUrl)
            : null;
    }

    public async Task InstallAsync(ReleaseInfo release, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(release.DownloadUrl))
        {
            throw new InvalidOperationException($"La release {release.TagName} non contiene l'asset {AssetName}.");
        }

        var packagePath = Path.Combine(Path.GetTempPath(), AssetName);
        await using (var input = await _httpClient.GetStreamAsync(release.DownloadUrl, cancellationToken))
        await using (var output = File.Create(packagePath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        // Non essendo ancora firmato digitalmente (vedi RIEPILOGO-SVILUPPO.md), l'unica difesa contro un
        // download corrotto o un asset alterato è verificarne il checksum SHA-256 pubblicato dalla
        // pipeline di release accanto allo zip, prima di scompattarlo sopra l'installazione esistente.
        if (string.IsNullOrWhiteSpace(release.ChecksumUrl))
        {
            File.Delete(packagePath);
            throw new InvalidOperationException(
                $"La release {release.TagName} non pubblica il checksum {ChecksumAssetName}: impossibile verificarne l'integrità, aggiornamento annullato.");
        }

        var expectedHash = (await _httpClient.GetStringAsync(release.ChecksumUrl, cancellationToken)).Trim();
        string actualHash;
        await using (var packageStream = File.OpenRead(packagePath))
        {
            actualHash = Convert.ToHexString(await SHA256.HashDataAsync(packageStream, cancellationToken));
        }

        if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(packagePath);
            throw new InvalidOperationException(
                $"Il pacchetto scaricato per {release.TagName} non corrisponde al checksum pubblicato: potrebbe essere corrotto o alterato. Aggiornamento annullato.");
        }

        var applicationPath = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var scriptPath = Path.Combine(Path.GetTempPath(), $"crmmes-update-{Guid.NewGuid():N}.cmd");
        var script = $"@echo off\r\ntimeout /t 2 /nobreak > nul\r\npowershell -NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '{packagePath.Replace("'", "''")}' -DestinationPath '{applicationPath.Replace("'", "''")}' -Force\"\r\ndel \"%~f0\"\r\n";
        await File.WriteAllTextAsync(scriptPath, script, Encoding.ASCII, cancellationToken);

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WorkingDirectory = applicationPath
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
