using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace CrmMes.Desktop;

/// <summary>Errors the program met, one file a day in %LOCALAPPDATA%\CrmMes\logs, kept two weeks: what an
/// assistant needs to see what went wrong on this PC. Never passwords or tokens: only the error.</summary>
public static class ClientLog
{
    public static string Directory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrmMes", "logs");

    private static readonly object Gate = new();

    public static void Error(string where, Exception exception) => Write("ERRORE", $"{where}: {exception}");

    public static void Info(string message) => Write("INFO", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                System.IO.Directory.CreateDirectory(Directory);
                var file = Path.Combine(Directory, $"client-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}{Environment.NewLine}", Encoding.UTF8);
                foreach (var old in new DirectoryInfo(Directory).GetFiles("client-*.log").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-14)))
                {
                    old.Delete();
                }
            }
        }
        catch
        {
            // Logging must never break the program (full disk, locked file).
        }
    }
}

/// <summary>What goes into the diagnostic package, collected by the Teleassistenza window.</summary>
public sealed record SupportSnapshot(
    string ClientVersion,
    string OperatingSystem,
    string Runtime,
    string MachineName,
    string ServerAddress,
    bool ServerReachable,
    string? ServerVersion,
    string? Hosting,
    string? LoggedInRole,
    int OfflineQueueCount,
    JsonElement? ServerDiagnostics,
    DateTime CreatedAt);

/// <summary>The diagnostic package: a zip with a readable summary (diagnostica.json) and the program's error
/// logs of the last week, to send to the assistant. Contains no password, token or company data.</summary>
public static class SupportPackage
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string ClientVersion =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)
        ?? "sconosciuta";

    public static SupportSnapshot Snapshot(
        string serverAddress, bool reachable, string? serverVersion, string? hosting, string? role, int offlineQueue, JsonElement? diagnostics) =>
        new(ClientVersion, RuntimeInformation.OSDescription, RuntimeInformation.FrameworkDescription, Environment.MachineName,
            serverAddress, reachable, serverVersion, hosting, role, offlineQueue, diagnostics, DateTime.Now);

    public static string Create(SupportSnapshot snapshot, string targetDirectory, string logsDirectory)
    {
        System.IO.Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, $"NicoloMES-diagnostica-{snapshot.MachineName}-{snapshot.CreatedAt:yyyyMMdd-HHmmss}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var summary = zip.CreateEntry("diagnostica.json");
        using (var writer = new StreamWriter(summary.Open(), new UTF8Encoding(false)))
        {
            writer.Write(JsonSerializer.Serialize(snapshot, Json));
        }

        if (System.IO.Directory.Exists(logsDirectory))
        {
            foreach (var log in new DirectoryInfo(logsDirectory).GetFiles("client-*.log").Where(f => f.LastWriteTime >= DateTime.Now.AddDays(-7)))
            {
                zip.CreateEntryFromFile(log.FullName, "log/" + log.Name);
            }
        }

        return path;
    }
}

/// <summary>Remote-control session with RustDesk (free and open source, can run on the assistant's own
/// server). Finds it installed or next to this program; otherwise the official download page.</summary>
public static class RustDesk
{
    public const string DownloadPage = "https://rustdesk.com/download";

    public static IEnumerable<string> Candidates(string? programFiles = null, string? programFilesX86 = null, string? localAppData = null, string? appDirectory = null)
    {
        programFiles ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        programFilesX86 ??= Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        localAppData ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        appDirectory ??= AppContext.BaseDirectory;
        yield return Path.Combine(programFiles, "RustDesk", "rustdesk.exe");
        yield return Path.Combine(programFilesX86, "RustDesk", "rustdesk.exe");
        yield return Path.Combine(localAppData, "Programs", "RustDesk", "rustdesk.exe");
        yield return Path.Combine(localAppData, "CrmMes", "support", "rustdesk.exe");
        yield return Path.Combine(appDirectory, "rustdesk.exe");
    }

    public static string? Find(IEnumerable<string>? candidates = null) =>
        (candidates ?? Candidates()).FirstOrDefault(File.Exists);

    /// <summary>True when RustDesk was started; false when it isn't there (the caller offers the download).</summary>
    public static bool TryStart()
    {
        var path = Find();
        if (path is null)
        {
            return false;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        return true;
    }

    public static void OpenDownloadPage() => Process.Start(new ProcessStartInfo(DownloadPage) { UseShellExecute = true });
}
