using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace CrmMes.Desktop.Tests;

public class RemoteSupportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nicolomes-support-" + Guid.NewGuid().ToString("N"));

    public RemoteSupportTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Package_HoldsTheSummaryAndRecentLogs_ButNoOldLogs()
    {
        var logs = Directory.CreateDirectory(Path.Combine(_root, "logs")).FullName;
        File.WriteAllText(Path.Combine(logs, "client-20260930.log"), "2026-09-30 [ERRORE] prova");
        var old = Path.Combine(logs, "client-20260901.log");
        File.WriteAllText(old, "vecchio");
        File.SetLastWriteTime(old, DateTime.Now.AddDays(-20));
        var snapshot = SupportPackage.Snapshot("https://server.example.test/", true, "1.6.0", "cloud", "Admin", 2, null);

        var path = SupportPackage.Create(snapshot, Path.Combine(_root, "out"), logs);

        using var zip = ZipFile.OpenRead(path);
        var names = zip.Entries.Select(e => e.FullName.Replace(Path.DirectorySeparatorChar, '/')).ToList();
        Assert.Contains("diagnostica.json", names);
        Assert.Contains("log/client-20260930.log", names);
        Assert.DoesNotContain("log/client-20260901.log", names);
        using var reader = new StreamReader(zip.GetEntry("diagnostica.json")!.Open());
        var text = reader.ReadToEnd();
        var json = JsonDocument.Parse(text).RootElement;
        Assert.Equal("https://server.example.test/", json.GetProperty("serverAddress").GetString());
        Assert.Equal(2, json.GetProperty("offlineQueueCount").GetInt32());
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Log_WritesOneFileADay_AndNeverThrows()
    {
        ClientLog.Directory = Path.Combine(_root, "clientlog");

        ClientLog.Error("prova", new InvalidOperationException("qualcosa non va"));
        ClientLog.Info("avvio");

        var file = Directory.GetFiles(ClientLog.Directory, "client-*.log").Single();
        var text = File.ReadAllText(file);
        Assert.Contains("[ERRORE] prova: System.InvalidOperationException: qualcosa non va", text);
        Assert.Contains("[INFO] avvio", text);
    }

    [Fact]
    public void RustDesk_IsFoundInstalled_OrReportedMissing()
    {
        var programFiles = Directory.CreateDirectory(Path.Combine(_root, "pf")).FullName;
        var candidates = RustDesk.Candidates(programFiles, Path.Combine(_root, "pf86"), Path.Combine(_root, "local"), Path.Combine(_root, "app")).ToList();
        Assert.Null(RustDesk.Find(candidates));

        Directory.CreateDirectory(Path.Combine(programFiles, "RustDesk"));
        var exe = Path.Combine(programFiles, "RustDesk", "rustdesk.exe");
        File.WriteAllText(exe, "");

        Assert.Equal(exe, RustDesk.Find(candidates));
    }
}
