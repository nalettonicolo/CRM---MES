using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Read-only per-serial traceability view: which units a work order produced and whether each
/// shipped good or was scrapped. Empty when the order's quantity wasn't a whole number at creation (see
/// WorkOrderUnit) — there's nothing discrete to list for a continuous/bulk quantity.</summary>
public partial class WorkOrderUnitsWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;

    public WorkOrderUnitsWindow(ApiClient apiClient, Guid workOrderId, string orderCode)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        OrderCodeText.Text = orderCode;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var units = await _apiClient.GetWorkOrderUnitsAsync(_workOrderId);
            if (units.Count == 0)
            {
                InfoText.Text = "Questa commessa non ha unità tracciate singolarmente: la quantità pianificata non era un numero intero di pezzi alla creazione.";
                return;
            }

            InfoText.Text = $"{units.Count} unità pianificate. Ogni unità nasce \"In attesa\": diventa \"Scartata\" se le si registra contro una non conformità, oppure \"Buona\" automaticamente al completamento della commessa se non è mai stata scartata. Doppio click su una riga per il dettaglio (fasi attraversate e lotti materiali attribuiti).";
            UnitsList.ItemsSource = units;
        }
        catch (Exception exception)
        {
            InfoText.Text = exception.Message;
        }
    }

    private void UnitsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (UnitsList.SelectedItem is not WorkOrderUnitDto unit)
        {
            return;
        }

        new WorkOrderUnitDetailWindow(_apiClient, _workOrderId, unit.Id) { Owner = this }.ShowDialog();
    }
}
