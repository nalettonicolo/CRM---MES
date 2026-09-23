using System.IO;
using System.Net.Http;
using CrmMes.Desktop;

namespace CrmMes.Desktop.Tests;

/// <summary>Every test uses its own temp file path (never the real %LOCALAPPDATA%\CrmMes\offline-queue.json)
/// so a test run can never clobber real queued terminal actions.</summary>
public class OfflineActionQueueTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"crmmes-offline-queue-test-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Enqueue_NewInstanceWithSamePath_ReloadsPersistedAction()
    {
        var queue = new OfflineActionQueue(_path);
        queue.Enqueue("TestKind", new { Value = 42 }, "Azione di test");

        var reloaded = new OfflineActionQueue(_path);

        Assert.Equal(1, reloaded.Count);
        Assert.Equal("TestKind", reloaded.PendingActions[0].Kind);
        Assert.Equal("Azione di test", reloaded.PendingActions[0].Description);
    }

    [Fact]
    public async Task SyncAsync_HandlerSucceeds_RemovesActionAndReportsSynced()
    {
        var queue = new OfflineActionQueue(_path);
        queue.Enqueue("Ok", new { }, "Azione riuscita");
        queue.RegisterHandler("Ok", (_, _) => Task.CompletedTask);

        var result = await queue.SyncAsync();

        Assert.Equal(1, result.Synced);
        Assert.Empty(result.Rejected);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task SyncAsync_HandlerThrowsHttpRequestException_StopsAndKeepsActionQueued()
    {
        var queue = new OfflineActionQueue(_path);
        queue.Enqueue("StillOffline", new { }, "Azione ancora offline");
        queue.RegisterHandler("StillOffline", (_, _) => throw new HttpRequestException("no connection"));

        var result = await queue.SyncAsync();

        Assert.Equal(0, result.Synced);
        Assert.Empty(result.Rejected);
        Assert.Equal(1, queue.Count);
    }

    [Fact]
    public async Task SyncAsync_HandlerThrowsInvalidOperationException_RemovesAndReportsRejected()
    {
        var queue = new OfflineActionQueue(_path);
        queue.Enqueue("Stale", new { }, "Azione ormai non valida");
        queue.RegisterHandler("Stale", (_, _) => throw new InvalidOperationException("La fase è già stata completata."));

        var result = await queue.SyncAsync();

        Assert.Equal(0, result.Synced);
        Assert.Single(result.Rejected);
        Assert.Equal("La fase è già stata completata.", result.Rejected[0].Error);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public async Task SyncAsync_PreservesOrder_ConnectivityFailureStopsBeforeLaterActions()
    {
        var queue = new OfflineActionQueue(_path);
        queue.Enqueue("First", new { }, "Prima azione");
        queue.Enqueue("Second", new { }, "Seconda azione");

        var secondAttempted = false;
        queue.RegisterHandler("First", (_, _) => throw new HttpRequestException("no connection"));
        queue.RegisterHandler("Second", (_, _) => { secondAttempted = true; return Task.CompletedTask; });

        var result = await queue.SyncAsync();

        Assert.Equal(0, result.Synced);
        Assert.False(secondAttempted);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public async Task SyncAsync_NoHandlerRegistered_RemovesAndReportsRejected()
    {
        var queue = new OfflineActionQueue(_path);
        queue.Enqueue("Unknown", new { }, "Azione senza gestore");

        var result = await queue.SyncAsync();

        Assert.Single(result.Rejected);
        Assert.Equal(0, queue.Count);
    }
}
