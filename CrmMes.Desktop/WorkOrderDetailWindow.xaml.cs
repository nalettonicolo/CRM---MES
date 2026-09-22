using System.Windows;

namespace CrmMes.Desktop;

public partial class WorkOrderDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly IReadOnlyDictionary<Guid, string> _productNames;
    private static readonly StatusToBrushConverter StatusBrush = new();

    public WorkOrderDetailWindow(ApiClient apiClient, Guid workOrderId, IReadOnlyDictionary<Guid, string> productNames)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        _productNames = productNames;
        Loaded += WorkOrderDetailWindow_Loaded;
    }

    private async void WorkOrderDetailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var order = await _apiClient.GetWorkOrderAsync(_workOrderId);

            CodeText.Text = order.Code;
            LotNumberText.Text = $"Lotto {order.ProductLotNumber}";
            StatusText.Text = StatusToItalianTextConverter.Translate(order.Status);
            StatusPill.Background = (System.Windows.Media.Brush)StatusBrush.Convert(order.Status, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;
            StatusText.Foreground = (System.Windows.Media.Brush)StatusBrush.Convert(order.Status, typeof(System.Windows.Media.Brush), "Foreground", System.Globalization.CultureInfo.CurrentCulture)!;

            ProductText.Text = _productNames.GetValueOrDefault(order.ProductId, order.ProductId.ToString());
            QuantityText.Text = order.Quantity.ToString();
            DueDateText.Text = order.DueDate?.ToLocalTime().ToString("d") ?? "-";
            CustomerReferenceText.Text = string.IsNullOrWhiteSpace(order.CustomerReference) ? "-" : order.CustomerReference;
            CreatedAtText.Text = order.CreatedAt.ToLocalTime().ToString("g");
            NotesText.Text = string.IsNullOrWhiteSpace(order.Notes) ? "-" : order.Notes;

            OperationsList.ItemsSource = order.Operations;

            ScheduleButton.IsEnabled = order.Status is not ("Completed" or "Cancelled");
            ReleaseButton.IsEnabled = order.Status == "Draft";
            CompleteButton.IsEnabled = order.Status is "Released" or "InProgress";
            GenerateSlipButton.IsEnabled = order.Status is not ("Completed" or "Cancelled");
            CancelButton.IsEnabled = order.Status is not ("Completed" or "Cancelled");

            LoadingText.Visibility = Visibility.Collapsed;
            HeaderInfo.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            LoadingText.Text = $"Impossibile caricare la commessa: {exception.Message}";
        }
    }

    private async void OperationAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkOrderOperationDto operation })
        {
            return;
        }

        ErrorText.Text = string.Empty;
        try
        {
            if (operation.Status == "Pending")
            {
                await _apiClient.StartOperationAsync(_workOrderId, operation.Id);
            }
            else if (operation.Status == "InProgress")
            {
                await _apiClient.CompleteOperationAsync(_workOrderId, operation.Id);
            }

            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Downtimes_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkOrderOperationDto operation })
        {
            return;
        }

        var window = new OperationDowntimesWindow(_apiClient, _workOrderId, operation.Id, operation.Name) { Owner = this };
        window.ShowDialog();
        await ReloadAsync();
    }

    private async void Release_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            await _apiClient.ReleaseWorkOrderAsync(_workOrderId);
            await ReloadAsync();
        }
        catch (MaterialShortfallException shortfall)
        {
            var lines = string.Join("\n", shortfall.Availability.Lines
                .Where(line => line.Shortfall > 0)
                .Select(line => $"- {line.MaterialCode}: richiesti {line.Required}, disponibili {line.Available} (mancano {line.Shortfall})"));

            var proceed = MessageBox.Show(
                $"Materiali insufficienti per questa commessa:\n{lines}\n\nRilasciare comunque?",
                "Materiali insufficienti", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (proceed != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await _apiClient.ReleaseWorkOrderAsync(_workOrderId, force: true);
                await ReloadAsync();
            }
            catch (Exception exception)
            {
                ErrorText.Text = exception.Message;
            }
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Schedule_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            await _apiClient.ScheduleWorkOrderAsync(_workOrderId);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Traceability_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var lots = await _apiClient.GetWorkOrderMaterialLotsAsync(_workOrderId);
            if (lots.Count == 0)
            {
                MessageBox.Show(
                    "Nessun lotto materiale ancora consumato da questa commessa (la distinta di prelievo generata dalla commessa deve essere chiusa perché lo scarico, e quindi il consumo dei lotti, avvenga).",
                    "Tracciabilità materiali", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var lines = lots.Select(lot => $"- {lot.MaterialCode} · lotto {lot.LotNumber}: {lot.QuantityConsumed} (distinta {lot.WithdrawalSlipCode})");
            MessageBox.Show(string.Join("\n", lines), "Lotti materiali consumati", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Complete_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            await _apiClient.CompleteWorkOrderAsync(_workOrderId);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void GenerateSlip_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        try
        {
            var slip = await _apiClient.GenerateWithdrawalSlipAsync(_workOrderId);
            MessageBox.Show(
                $"Distinta di prelievo {slip.WithdrawalSlipCode} generata. La trovi nella scheda \"Distinte di prelievo\".",
                "Distinta generata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"Annullare la commessa {CodeText.Text}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        ErrorText.Text = string.Empty;
        try
        {
            await _apiClient.CancelWorkOrderAsync(_workOrderId);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
