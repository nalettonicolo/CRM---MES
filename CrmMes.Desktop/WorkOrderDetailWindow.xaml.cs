using System.Windows;

namespace CrmMes.Desktop;

public partial class WorkOrderDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly IReadOnlyDictionary<Guid, string> _productNames;
    private Guid _productId;
    private static readonly StatusToBrushConverter StatusBrush = new();

    public WorkOrderDetailWindow(ApiClient apiClient, Guid workOrderId, IReadOnlyDictionary<Guid, string> productNames)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        _productNames = productNames;
        Loaded += WorkOrderDetailWindow_Loaded;
        // Costs and margins are management-only; the API refuses them to other roles anyway.
        CostButton.Visibility = apiClient.CanViewMargins ? Visibility.Visible : Visibility.Collapsed;
        TransportDocumentButton.Visibility = apiClient.IsModuleEnabled("shipping") ? Visibility.Visible : Visibility.Collapsed;
        // Panel builders only: the CEI EN 61439 routine verification and declaration.
        PanelVerificationButton.Visibility = apiClient.IsModuleEnabled("panel-verification") ? Visibility.Visible : Visibility.Collapsed;
        MachineTestingButton.Visibility = apiClient.IsModuleEnabled("machine-testing") ? Visibility.Visible : Visibility.Collapsed;
        FoodLabelButton.Visibility = PalletButton.Visibility = apiClient.IsModuleEnabled("food-labels") ? Visibility.Visible : Visibility.Collapsed;
        SiteReportButton.Visibility = apiClient.IsModuleEnabled("site-work") ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Food label of this lot: warnings (missing food data) are shown before printing.</summary>
    private async void FoodLabel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var label = await _apiClient.GetFoodLabelAsync(_workOrderId);
            if (label.Warnings.Count > 0 && MessageBox.Show(
                    "Attenzione:\n\n" + string.Join("\n", label.Warnings.Select(w => "• " + w)) + "\n\nStampare comunque l'etichetta?",
                    "Etichetta", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            SavePdf($"Etichetta-{label.LotNumber ?? label.WorkOrderCode}.pdf", path => ListExporter.ExportFoodLabel(label, path));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Etichetta", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Allocates the next SSCC for a pallet of this lot and prints its GS1 logistic label.</summary>
    private async void Pallet_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("Pallet SSCC", "Quantità sul pallet (vuoto = intera commessa). Verrà assegnato un nuovo codice SSCC.") { Owner = this };
        prompt.AllowEmpty = true;
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        decimal? quantity = null;
        if (!string.IsNullOrWhiteSpace(prompt.Value))
        {
            if (!NumberInput.TryParseDecimal(prompt.Value, out var parsed) || parsed <= 0)
            {
                MessageBox.Show("Quantità non valida.", "Pallet SSCC", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            quantity = parsed;
        }

        try
        {
            var unit = await _apiClient.CreateLogisticUnitAsync(_workOrderId, null, quantity);
            SavePdf($"SSCC-{unit.Sscc}.pdf", path => ListExporter.ExportPalletLabel(unit, _apiClient.CompanyProfile, path));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Pallet SSCC", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SiteReport_Click(object sender, RoutedEventArgs e) =>
        new SiteReportWindow(_apiClient, workOrderId: _workOrderId) { Owner = this }.ShowDialog();

    private void SavePdf(string fileName, Action<string> export)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = "File PDF (*.pdf)|*.pdf" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        export(dialog.FileName);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
    }

    private void PanelVerification_Click(object sender, RoutedEventArgs e) =>
        new PanelVerificationWindow(_apiClient, _workOrderId) { Owner = this }.ShowDialog();

    private void MachineTesting_Click(object sender, RoutedEventArgs e) =>
        new MachineTestingWindow(_apiClient, _workOrderId) { Owner = this }.ShowDialog();

    /// <summary>A DDT draft prefilled with this job's customer, product, lot and quantity.</summary>
    private async void TransportDocument_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var draft = await _apiClient.CreateTransportDocumentFromWorkOrderAsync(_workOrderId);
            new TransportDocumentWindow(_apiClient, draft.Id) { Owner = this }.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "DDT", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e) => CopySupport.Copy(CodeText.Text);

    private void CopyLot_Click(object sender, RoutedEventArgs e) => CopySupport.Copy(LotNumberText.Text);

    private void Cost_Click(object sender, RoutedEventArgs e) =>
        new WorkOrderCostWindow(_apiClient, _workOrderId) { Owner = this }.ShowDialog();

    private void Labor_Click(object sender, RoutedEventArgs e) =>
        new LaborEntriesWindow(_apiClient, _workOrderId) { Owner = this }.ShowDialog();

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
            LotNumberText.Text = order.ProductLotNumber;
            StatusText.Text = StatusToItalianTextConverter.Translate(order.Status);
            StatusPill.Background = (System.Windows.Media.Brush)StatusBrush.Convert(order.Status, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;
            StatusText.Foreground = (System.Windows.Media.Brush)StatusBrush.Convert(order.Status, typeof(System.Windows.Media.Brush), "Foreground", System.Globalization.CultureInfo.CurrentCulture)!;

            _productId = order.ProductId;
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

    private async void NonConformities_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkOrderOperationDto operation })
        {
            return;
        }

        var window = new NonConformitiesWindow(_apiClient, _workOrderId, operation.Id, operation.Name) { Owner = this };
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

    private void Label_Click(object sender, RoutedEventArgs e)
    {
        var window = new WorkOrderLabelWindow(CodeText.Text, ProductText.Text, LotNumberText.Text.Replace("Lotto ", string.Empty)) { Owner = this };
        window.ShowDialog();
    }

    private void Units_Click(object sender, RoutedEventArgs e)
    {
        var window = new WorkOrderUnitsWindow(_apiClient, _workOrderId, CodeText.Text) { Owner = this };
        window.ShowDialog();
    }

    private void Quality_Click(object sender, RoutedEventArgs e)
    {
        var window = new WorkOrderQualityWindow(_apiClient, _workOrderId, _productId, CodeText.Text) { Owner = this };
        window.ShowDialog();
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
