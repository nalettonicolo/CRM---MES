using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Opened either from the office client (no <paramref name="offlineQueue"/> — stays online-only)
/// or from the shop-floor terminal (passes its own OfflineActionQueue so registering a defect still works
/// with no connectivity), same pattern as OperationDowntimesWindow.</summary>
public partial class NonConformitiesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly Guid _operationId;
    private readonly string? _operatorName;
    private readonly Guid? _operatorId;
    private readonly OfflineActionQueue? _offlineQueue;

    private sealed record RegisterNonConformityPayload(
        Guid WorkOrderId, Guid OperationId, string Description, decimal ScrapQuantity, string? Notes,
        string? OperatorName, Guid? WorkOrderUnitId, Guid? OperatorId);

    public NonConformitiesWindow(ApiClient apiClient, Guid workOrderId, Guid operationId, string operationName,
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
        Loaded += NonConformitiesWindow_Loaded;
    }

    private async void NonConformitiesWindow_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        try
        {
            NonConformitiesList.ItemsSource = await _apiClient.GetNonConformitiesAsync(_workOrderId, _operationId);
            await ReloadUnitsAsync();
        }
        catch (HttpRequestException) when (_offlineQueue is not null)
        {
            // No connectivity: history/unit picker stay whatever was last loaded while online. A new
            // defect can still be registered without a unit selected — it queues below.
            ErrorText.Text = "Nessuna connessione: storico non aggiornato. Puoi comunque registrare una non conformità, verrà sincronizzata al ritorno della connessione.";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    /// <summary>Only shows the unit picker when the work order actually has per-serial units (its
    /// quantity was a whole number at creation — see WorkOrderUnit) and at least one is still Pending;
    /// otherwise there's nothing meaningful to scrap by unit and the picker would just be empty.</summary>
    private async Task ReloadUnitsAsync()
    {
        var units = await _apiClient.GetWorkOrderUnitsAsync(_workOrderId);
        var pendingUnits = units.Where(u => u.Status == "Pending").ToList();

        if (pendingUnits.Count == 0)
        {
            UnitPickerPanel.Visibility = Visibility.Collapsed;
            UnitCombo.ItemsSource = null;
            return;
        }

        UnitPickerPanel.Visibility = Visibility.Visible;
        UnitCombo.ItemsSource = pendingUnits;
    }

    private async void RegisterNonConformity_Click(object sender, RoutedEventArgs e)
    {
        var description = DescriptionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            ErrorText.Text = "Indica la descrizione della non conformità.";
            return;
        }

        if (!decimal.TryParse(ScrapQuantityBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var scrapQuantity) || scrapQuantity <= 0)
        {
            ErrorText.Text = "Indica una quantità scarto maggiore di zero.";
            return;
        }

        var selectedUnitId = (UnitCombo.SelectedItem as WorkOrderUnitDto)?.Id;
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        try
        {
            await _apiClient.RegisterNonConformityAsync(_workOrderId, _operationId, description, scrapQuantity,
                notes, _operatorName, selectedUnitId, _operatorId);
            DescriptionBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            ScrapQuantityBox.Text = "1";
            ErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (HttpRequestException) when (_offlineQueue is not null)
        {
            var payload = new RegisterNonConformityPayload(
                _workOrderId, _operationId, description, scrapQuantity, notes, _operatorName, selectedUnitId, _operatorId);
            _offlineQueue.Enqueue("RegisterNonConformity", payload, $"Non conformità \"{description}\"");
            DescriptionBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            ScrapQuantityBox.Text = "1";
            ErrorText.Text = "Nessuna connessione: non conformità messa in coda, verrà sincronizzata automaticamente.";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    /// <summary>Registers the handler this window's queued action needs — called once by whoever owns
    /// the OfflineActionQueue (the shop-floor terminal).</summary>
    public static void RegisterOfflineHandlers(OfflineActionQueue queue, ApiClient apiClient)
    {
        queue.RegisterHandler("RegisterNonConformity", async (json, ct) =>
        {
            var payload = JsonSerializer.Deserialize<RegisterNonConformityPayload>(json)!;
            await apiClient.RegisterNonConformityAsync(
                payload.WorkOrderId, payload.OperationId, payload.Description, payload.ScrapQuantity,
                payload.Notes, payload.OperatorName, payload.WorkOrderUnitId, payload.OperatorId, ct);
        });
    }
}
