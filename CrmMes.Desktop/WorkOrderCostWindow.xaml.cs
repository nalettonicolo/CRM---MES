using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Costo stimato vs reale e margine di una commessa (Admin e Management). Il prezzo di vendita
/// si imposta da qui per le commesse nate a mano; quelle convertite da preventivo lo hanno già.</summary>
public partial class WorkOrderCostWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private static readonly Brush LossBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0x39, 0x2B));
    private static readonly Brush LowBrush = new SolidColorBrush(Color.FromRgb(0xA1, 0x5C, 0x07));
    private static readonly Brush GoodBrush = new SolidColorBrush(Color.FromRgb(0x2F, 0x6B, 0x3F));

    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;

    public WorkOrderCostWindow(ApiClient apiClient, Guid workOrderId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var costing = await _apiClient.GetWorkOrderCostingAsync(_workOrderId);
            Title = $"Costi e margine · {costing.WorkOrderCode}";
            HeaderText.Text = $"Costi e margine · {costing.WorkOrderCode}";
            SubHeaderText.Text = $"{costing.ProductCode} · {costing.ProductName} · quantità {costing.Quantity.ToString("0.##", Italian)} · " +
                                 StatusToItalianTextConverter.Translate(costing.Status);

            SalePriceBox.Text = costing.SalePrice?.ToString("0.00", Italian) ?? string.Empty;
            SalePriceNote.Text = costing.EstimatedMargin?.Ratio is { } estimatedRatio
                ? $"Margine previsto {estimatedRatio.ToString("P1", Italian)}"
                : "Imponibile concordato con il cliente.";

            EstimatedText.Text = Euro(costing.Estimated.Total);
            EstimatedBreakdown.Text = $"Materiali {Euro(costing.Estimated.Material)} · manodopera {Euro(costing.Estimated.Labor)}";
            ActualText.Text = Euro(costing.Actual.Total);
            ActualBreakdown.Text =
                $"Materiali {Euro(costing.Actual.Material)} · manodopera {Euro(costing.Actual.Labor)} ({Math.Floor(costing.ActualMinutes / 60):0}h {costing.ActualMinutes % 60:00}m)";

            if (costing.ActualMargin is { } margin)
            {
                MarginText.Text = Euro(margin.Amount);
                MarginText.Foreground = margin.Ratio switch
                {
                    < 0 => LossBrush,
                    < 0.15m => LowBrush,
                    _ => GoodBrush
                };
                MarginNote.Text = margin.Ratio is { } ratio ? $"{ratio.ToString("P1", Italian)} del prezzo di vendita" : string.Empty;
            }
            else
            {
                MarginText.Text = "-";
                MarginText.ClearValue(ForegroundProperty);
                MarginNote.Text = "Serve il prezzo di vendita.";
            }

            WarningsList.ItemsSource = costing.Warnings;
            WarningsBorder.Visibility = costing.Warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            MaterialsList.ItemsSource = costing.Materials;
            LaborList.ItemsSource = costing.Labor;
            ErrorText.Text = string.Empty;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void SavePrice_Click(object sender, RoutedEventArgs e)
    {
        decimal? price = null;
        if (!string.IsNullOrWhiteSpace(SalePriceBox.Text))
        {
            if (!NumberInput.TryParseDecimal(SalePriceBox.Text, out var parsed) || parsed < 0)
            {
                ErrorText.Text = "Prezzo di vendita non valido.";
                return;
            }

            price = parsed;
        }

        SavePriceButton.IsEnabled = false;
        try
        {
            await _apiClient.SetWorkOrderSalePriceAsync(_workOrderId, price);
            await LoadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SavePriceButton.IsEnabled = true;
        }
    }

    private async void Labor_Click(object sender, RoutedEventArgs e)
    {
        new LaborEntriesWindow(_apiClient, _workOrderId) { Owner = this }.ShowDialog();
        await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private static string Euro(decimal value) => $"{value.ToString("N2", Italian)} €";
}
