using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Opened either from the office client (WorkOrderDetailWindow, no <paramref name="offlineQueue"/> —
/// stays online-only, same as the rest of the office client) or from the shop-floor terminal (which passes
/// its own OfflineActionQueue so starting/ending a stop still works with no connectivity, same pattern as
/// the terminal's own start/complete-phase actions). Ending a stop needs a server-assigned downtime id, so
/// it can only be queued if the open downtime was already loaded while still online — nothing to attach an
/// "end" to otherwise.</summary>
public partial class OperationDowntimesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly Guid _operationId;
    private readonly string? _operatorName;
    private readonly Guid? _operatorId;
    private readonly OfflineActionQueue? _offlineQueue;
    private OperationDowntimeDto? _openDowntime;

    private sealed record StartDowntimePayload(Guid WorkOrderId, Guid OperationId, string Reason, string? Notes, string? OperatorName, Guid? OperatorId);
    private sealed record EndDowntimePayload(Guid WorkOrderId, Guid OperationId, Guid DowntimeId, string? OperatorName, Guid? OperatorId);

    public OperationDowntimesWindow(ApiClient apiClient, Guid workOrderId, Guid operationId, string operationName,
        string? operatorName = null, Guid? operatorId = null, OfflineActionQueue? offlineQueue = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        _operationId = operationId;
        _operatorName = operatorName;
        _operatorId = operatorId;
        _offlineQueue = offlineQueue;
        OperationNameText.Text = operationName;
        Loaded += OperationDowntimesWindow_Loaded;
    }

    private async void OperationDowntimesWindow_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        try
        {
            var downtimes = await _apiClient.GetOperationDowntimesAsync(_workOrderId, _operationId);
            DowntimesList.ItemsSource = downtimes;

            _openDowntime = downtimes.FirstOrDefault(d => d.EndedAt is null);
            if (_openDowntime is not null)
            {
                OpenDowntimePanel.Visibility = Visibility.Visible;
                OpenDowntimeText.Text = $"Fermo aperto dalle {_openDowntime.StartedAt.ToLocalTime():t}: {_openDowntime.Reason}";
                NewDowntimePanel.IsEnabled = false;
            }
            else
            {
                OpenDowntimePanel.Visibility = Visibility.Collapsed;
                NewDowntimePanel.IsEnabled = true;
            }
        }
        catch (HttpRequestException) when (_offlineQueue is not null)
        {
            // No connectivity and no way to know whether a stop is already open from here — the form
            // stays usable (a new stop can still be queued) but the history/open-stop state shown is
            // just whatever was last loaded while online, not necessarily current.
            ErrorText.Text = "Nessuna connessione: storico non aggiornato. Puoi comunque segnalare un fermo, verrà sincronizzato al ritorno della connessione.";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void StartDowntime_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReasonBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorText.Text = "Indica il motivo del fermo.";
            return;
        }

        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();
        try
        {
            await _apiClient.StartDowntimeAsync(_workOrderId, _operationId, reason, notes, _operatorName, _operatorId);
            ReasonBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            ErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (HttpRequestException) when (_offlineQueue is not null)
        {
            var payload = new StartDowntimePayload(_workOrderId, _operationId, reason, notes, _operatorName, _operatorId);
            _offlineQueue.Enqueue("StartDowntime", payload, $"Avvio fermo \"{reason}\"");
            ReasonBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            ErrorText.Text = "Nessuna connessione: fermo messo in coda, verrà sincronizzato automaticamente.";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void EndDowntime_Click(object sender, RoutedEventArgs e)
    {
        if (_openDowntime is null)
        {
            return;
        }

        try
        {
            await _apiClient.EndDowntimeAsync(_workOrderId, _operationId, _openDowntime.Id, _operatorName, _operatorId);
            await ReloadAsync();
        }
        catch (HttpRequestException) when (_offlineQueue is not null)
        {
            var payload = new EndDowntimePayload(_workOrderId, _operationId, _openDowntime.Id, _operatorName, _operatorId);
            _offlineQueue.Enqueue("EndDowntime", payload, $"Chiusura fermo — {_openDowntime.Reason}");
            ErrorText.Text = "Nessuna connessione: chiusura fermo messa in coda, verrà sincronizzata automaticamente.";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    /// <summary>Registers the handlers this window's queued actions need — called once by whoever owns
    /// the OfflineActionQueue (the shop-floor terminal), same pattern as its own operation handlers.</summary>
    public static void RegisterOfflineHandlers(OfflineActionQueue queue, ApiClient apiClient)
    {
        queue.RegisterHandler("StartDowntime", async (json, ct) =>
        {
            var payload = JsonSerializer.Deserialize<StartDowntimePayload>(json)!;
            await apiClient.StartDowntimeAsync(payload.WorkOrderId, payload.OperationId, payload.Reason, payload.Notes, payload.OperatorName, payload.OperatorId, ct);
        });
        queue.RegisterHandler("EndDowntime", async (json, ct) =>
        {
            var payload = JsonSerializer.Deserialize<EndDowntimePayload>(json)!;
            await apiClient.EndDowntimeAsync(payload.WorkOrderId, payload.OperationId, payload.DowntimeId, payload.OperatorName, payload.OperatorId, ct);
        });
    }
}
