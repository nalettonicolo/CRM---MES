using System.Globalization;
using System.Windows;

namespace CrmMes.Desktop;

public partial class WorkOrderQualityWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly Guid _productId;

    public WorkOrderQualityWindow(ApiClient apiClient, Guid workOrderId, Guid productId, string orderCode)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        _productId = productId;
        OrderTitleText.Text = orderCode;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var checkpoints = await _apiClient.GetQualityCheckpointsAsync(_productId);
            CheckpointCombo.ItemsSource = checkpoints;
            InfoText.Text = checkpoints.Count == 0
                ? "Questo prodotto non ha ancora un piano di controllo qualità: aggiungine uno dalla scheda del prodotto."
                : $"{checkpoints.Count} caratteristiche nel piano di controllo di questo prodotto.";

            MeasurementsList.ItemsSource = await _apiClient.GetQualityMeasurementsAsync(_workOrderId);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void RecordMeasurement_Click(object sender, RoutedEventArgs e)
    {
        if (CheckpointCombo.SelectedItem is not QualityCheckpointDto checkpoint)
        {
            ErrorText.Text = "Seleziona una caratteristica.";
            return;
        }

        if (!decimal.TryParse(ValueBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            ErrorText.Text = "Inserisci un valore numerico valido.";
            return;
        }

        try
        {
            await _apiClient.RecordQualityMeasurementAsync(checkpoint.Id, _workOrderId, null, value, null, null);
            ValueBox.Text = string.Empty;
            ErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void GenerateCertificate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var certificate = await _apiClient.GetQualityCertificateAsync(_workOrderId);
            if (certificate.Measurements.Count == 0)
            {
                MessageBox.Show("Non ci sono misurazioni registrate per questa commessa.", "Certificato", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"Certificato-{certificate.WorkOrderCode}.pdf"
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ListExporter.ExportQualityCertificate(certificate, dialog.FileName);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
