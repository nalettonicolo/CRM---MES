using System.Windows;
using System.Windows.Input;

namespace CrmMes.Desktop;

/// <summary>A simplified, large-type kiosk view for the shop floor: an operator scans (or types) the
/// work order code printed on the job's <see cref="WorkOrderLabelWindow"/> label, then starts/completes
/// the next open phase, or logs a downtime/non-conformity against it — without navigating the full
/// office client. A barcode/QR scanner reads as a keyboard (types the code, then Enter), so this needs
/// no special hardware integration, just a focused text box.</summary>
public partial class ShopFloorTerminalWindow : Window
{
    private readonly ApiClient _apiClient;
    private static readonly StatusToBrushConverter StatusBrush = new();
    private WorkOrderDetailDto? _order;
    private WorkOrderOperationDto? _activeOperation;

    public ShopFloorTerminalWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += (_, _) => ScanBox.Focus();
    }

    private async void ScanBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LookupAsync();
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await LookupAsync();

    private async Task LookupAsync()
    {
        var code = ScanBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        ErrorText.Text = string.Empty;
        try
        {
            _order = await _apiClient.GetWorkOrderByCodeAsync(code);
            var product = await _apiClient.GetProductAsync(_order.ProductId);

            JobProductText.Text = $"{product.Name} · quantità {_order.Quantity}";
            ApplyOrderToUi();

            JobPanel.Visibility = Visibility.Visible;
            ScanBox.Text = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            JobPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            ScanBox.Focus();
        }
    }

    private void ApplyOrderToUi()
    {
        if (_order is null)
        {
            return;
        }

        JobCodeText.Text = _order.Code;
        JobLotText.Text = $"Lotto {_order.ProductLotNumber}";
        JobStatusText.Text = StatusToItalianTextConverter.Translate(_order.Status);
        JobStatusPill.Background = (System.Windows.Media.Brush)StatusBrush.Convert(_order.Status, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;
        JobStatusText.Foreground = (System.Windows.Media.Brush)StatusBrush.Convert(_order.Status, typeof(System.Windows.Media.Brush), "Foreground", System.Globalization.CultureInfo.CurrentCulture)!;

        OperationsList.ItemsSource = _order.Operations;
        _activeOperation = _order.Operations
            .OrderBy(op => op.SequenceNumber)
            .FirstOrDefault(op => op.Status is "Pending" or "InProgress");

        UpdateActionArea();
    }

    private void UpdateActionArea()
    {
        if (_activeOperation is null)
        {
            // Every operation is snapshotted as Pending as soon as the work order is created (even in
            // Draft), so landing here means every operation is already Done — completing the order
            // itself is a separate, explicit step (WorkOrderDetailWindow's "Completa"), not automatic.
            ActiveOperationText.Text = _order!.Status == "Completed"
                ? "Commessa completata."
                : "Tutte le fasi sono state completate. Completa la commessa dal client per chiuderla.";
            ActionButton.Content = "-";
            ActionButton.IsEnabled = false;
            DowntimeButton.IsEnabled = false;
            NonConformityButton.IsEnabled = false;
            return;
        }

        var workCenterSuffix = string.IsNullOrWhiteSpace(_activeOperation.WorkCenter) ? "" : $" ({_activeOperation.WorkCenter})";
        ActiveOperationText.Text = $"Fase corrente: {_activeOperation.Name}{workCenterSuffix}";
        ActionButton.Content = _activeOperation.Status == "Pending" ? "Avvia fase" : "Completa fase";
        ActionButton.IsEnabled = true;
        DowntimeButton.IsEnabled = _activeOperation.Status == "InProgress";
        NonConformityButton.IsEnabled = _activeOperation.Status != "Pending";
    }

    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null || _activeOperation is null)
        {
            return;
        }

        ErrorText.Text = string.Empty;
        try
        {
            if (_activeOperation.Status == "Pending")
            {
                await _apiClient.StartOperationAsync(_order.Id, _activeOperation.Id);
            }
            else
            {
                await _apiClient.CompleteOperationAsync(_order.Id, _activeOperation.Id);
            }

            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async Task RefreshAsync()
    {
        if (_order is null)
        {
            return;
        }

        _order = await _apiClient.GetWorkOrderAsync(_order.Id);
        ApplyOrderToUi();
    }

    private async void Downtime_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null || _activeOperation is null)
        {
            return;
        }

        var window = new OperationDowntimesWindow(_apiClient, _order.Id, _activeOperation.Id, _activeOperation.Name) { Owner = this };
        window.ShowDialog();
        await RefreshAsync();
    }

    private async void NonConformity_Click(object sender, RoutedEventArgs e)
    {
        if (_order is null || _activeOperation is null)
        {
            return;
        }

        var window = new NonConformitiesWindow(_apiClient, _order.Id, _activeOperation.Id, _activeOperation.Name) { Owner = this };
        window.ShowDialog();
        await RefreshAsync();
    }

    private void NewScan_Click(object sender, RoutedEventArgs e)
    {
        _order = null;
        _activeOperation = null;
        JobPanel.Visibility = Visibility.Collapsed;
        ErrorText.Text = string.Empty;
        ScanBox.Text = string.Empty;
        ScanBox.Focus();
    }
}
