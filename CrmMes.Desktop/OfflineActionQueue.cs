using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace CrmMes.Desktop;

public sealed record QueuedAction(string Id, string Kind, string PayloadJson, string Description, DateTime CreatedAt);

public sealed record SyncResult(int Synced, IReadOnlyList<(QueuedAction Action, string Error)> Rejected);

/// <summary>A small, generic offline write queue: when an action can't reach the API (no connectivity),
/// it's persisted here instead of failing outright, and replayed in order once the API is reachable
/// again. Built generic on purpose — any window can enqueue any kind of action by registering a replay
/// handler for its "kind" string — even though today only the shop-floor terminal's start/complete-phase
/// actions use it. Extending this to fermi macchina, non conformità, or the office client later is just
/// registering more handlers against the same queue, not building a new mechanism.</summary>
public sealed class OfflineActionQueue
{
    private readonly string _filePath;
    private readonly Dictionary<string, Func<string, CancellationToken, Task>> _handlers = new();
    private List<QueuedAction> _actions = [];

    public OfflineActionQueue(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrmMes", "offline-queue.json");
        Load();
    }

    public IReadOnlyList<QueuedAction> PendingActions => _actions;
    public int Count => _actions.Count;

    public void RegisterHandler(string kind, Func<string, CancellationToken, Task> handler) => _handlers[kind] = handler;

    public void Enqueue(string kind, object payload, string description)
    {
        _actions.Add(new QueuedAction(Guid.NewGuid().ToString("N"), kind, JsonSerializer.Serialize(payload), description, DateTime.UtcNow));
        Save();
    }

    /// <summary>Replays pending actions strictly in the order they were queued — later actions on the
    /// same entity often depend on an earlier one having landed first (e.g. you can't complete a phase
    /// whose start is still sitting in the queue). Stops at the first connectivity failure and leaves
    /// everything from that point queued for the next attempt. A definite rejection — the server is
    /// reachable but refuses the action, e.g. its state moved on while offline — removes just that one
    /// action and keeps going, since replaying it again unchanged could never succeed.</summary>
    public async Task<SyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var synced = 0;
        var rejected = new List<(QueuedAction, string)>();

        while (_actions.Count > 0)
        {
            var action = _actions[0];
            if (!_handlers.TryGetValue(action.Kind, out var handler))
            {
                rejected.Add((action, "Nessun gestore registrato per questa azione."));
                _actions.RemoveAt(0);
                continue;
            }

            try
            {
                await handler(action.PayloadJson, cancellationToken);
                _actions.RemoveAt(0);
                synced++;
            }
            catch (HttpRequestException)
            {
                break;
            }
            catch (InvalidOperationException exception)
            {
                rejected.Add((action, exception.Message));
                _actions.RemoveAt(0);
            }
        }

        Save();
        return new SyncResult(synced, rejected);
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _actions = JsonSerializer.Deserialize<List<QueuedAction>>(json) ?? [];
            }
        }
        catch (Exception)
        {
            _actions = [];
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(_actions));
        }
        catch (Exception)
        {
            // Best-effort persistence: if the disk write fails there's nothing more to do here — the
            // queue still works in-memory for the rest of this session.
        }
    }
}
