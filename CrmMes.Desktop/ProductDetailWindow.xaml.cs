using System.Collections.ObjectModel;
using System.Windows;

namespace CrmMes.Desktop;

public partial class ProductDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _productId;
    private readonly ObservableCollection<BomRow> _bomItems = new();
    private readonly ObservableCollection<RoutingRow> _routingSteps = new();
    private static readonly StatusToBrushConverter StatusBrush = new();

    /// <summary>True se, dopo la chiusura di questa finestra, la lista prodotti nella finestra
    /// principale va ricaricata (il prodotto è stato disattivato, oppure distinta base/ciclo di lavoro
    /// sono stati modificati, il che cambia i conteggi mostrati nell'elenco).</summary>
    public bool Changed { get; private set; }

    public ProductDetailWindow(ApiClient apiClient, Guid productId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _productId = productId;
        BomList.ItemsSource = _bomItems;
        RoutingList.ItemsSource = _routingSteps;
        Loaded += ProductDetailWindow_Loaded;
    }

    private async void ProductDetailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var product = await _apiClient.GetProductAsync(_productId);

            CodeText.Text = product.Code;
            NameText.Text = string.IsNullOrWhiteSpace(product.Description) ? product.Name : $"{product.Name} — {product.Description}";
            StatusText.Text = product.IsActive ? "Attivo" : "Inattivo";
            var statusKey = product.IsActive ? "Received" : "Cancelled";
            StatusPill.Background = (System.Windows.Media.Brush)StatusBrush.Convert(statusKey, typeof(System.Windows.Media.Brush), null, System.Globalization.CultureInfo.CurrentCulture)!;
            StatusText.Foreground = (System.Windows.Media.Brush)StatusBrush.Convert(statusKey, typeof(System.Windows.Media.Brush), "Foreground", System.Globalization.CultureInfo.CurrentCulture)!;
            DeactivateButton.IsEnabled = product.IsActive;

            _bomItems.Clear();
            foreach (var item in product.BillOfMaterial)
            {
                _bomItems.Add(new BomRow(item.MaterialCode, item.Quantity, item.Notes));
            }

            _routingSteps.Clear();
            foreach (var step in product.RoutingSteps.OrderBy(s => s.SequenceNumber))
            {
                _routingSteps.Add(new RoutingRow(step.SequenceNumber, step.Name, step.WorkCenter, step.EstimatedMinutes));
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AddBomItem_Click(object sender, RoutedEventArgs e)
    {
        var code = BomMaterialCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || !decimal.TryParse(BomQuantityBox.Text, out var quantity) || quantity <= 0)
        {
            BomErrorText.Text = "Inserisci un codice materiale e una quantità valida.";
            return;
        }

        _bomItems.Add(new BomRow(code, quantity, string.IsNullOrWhiteSpace(BomNotesBox.Text) ? null : BomNotesBox.Text.Trim()));
        BomMaterialCodeBox.Text = string.Empty;
        BomQuantityBox.Text = "1";
        BomNotesBox.Text = string.Empty;
        BomErrorText.Text = string.Empty;
        BomMaterialCodeBox.Focus();
    }

    private void RemoveBomItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: BomRow row })
        {
            _bomItems.Remove(row);
        }
    }

    private async void SaveBom_Click(object sender, RoutedEventArgs e)
    {
        if (_bomItems.Count == 0)
        {
            BomErrorText.Text = "Aggiungi almeno una riga materiale.";
            return;
        }

        SaveBomButton.IsEnabled = false;
        try
        {
            await _apiClient.ReplaceBillOfMaterialAsync(
                _productId,
                _bomItems.Select(item => (item.MaterialCode, item.Quantity, item.Notes)).ToList());
            Changed = true;
            BomErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            BomErrorText.Text = exception.Message;
        }
        finally
        {
            SaveBomButton.IsEnabled = true;
        }
    }

    private async void ImportBom_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "File distinta base (*.xlsx;*.csv)|*.xlsx;*.csv",
            Title = "Importa distinta base"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ImportBomButton.IsEnabled = false;
        try
        {
            await _apiClient.ImportBillOfMaterialAsync(_productId, dialog.FileName);
            Changed = true;
            BomErrorText.Text = string.Empty;
            await ReloadAsync();
            MessageBox.Show("Distinta base importata correttamente.", "Import completato", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            BomErrorText.Text = exception.Message;
        }
        finally
        {
            ImportBomButton.IsEnabled = true;
        }
    }

    private void AddRoutingStep_Click(object sender, RoutedEventArgs e)
    {
        var name = RoutingNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || !decimal.TryParse(RoutingMinutesBox.Text, out var minutes) || minutes < 0)
        {
            RoutingErrorText.Text = "Inserisci un nome fase e una durata stimata valida.";
            return;
        }

        var workCenter = string.IsNullOrWhiteSpace(RoutingWorkCenterBox.Text) ? null : RoutingWorkCenterBox.Text.Trim();
        _routingSteps.Add(new RoutingRow(_routingSteps.Count + 1, name, workCenter, minutes));
        RoutingNameBox.Text = string.Empty;
        RoutingWorkCenterBox.Text = string.Empty;
        RoutingMinutesBox.Text = "0";
        RoutingErrorText.Text = string.Empty;
        RoutingNameBox.Focus();
    }

    private void RemoveRoutingStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RoutingRow row })
        {
            return;
        }

        var remaining = _routingSteps.Where(step => step != row).ToList();
        _routingSteps.Clear();
        var sequence = 1;
        foreach (var step in remaining)
        {
            _routingSteps.Add(step with { Sequence = sequence++ });
        }
    }

    private async void SaveRouting_Click(object sender, RoutedEventArgs e)
    {
        if (_routingSteps.Count == 0)
        {
            RoutingErrorText.Text = "Aggiungi almeno una fase.";
            return;
        }

        SaveRoutingButton.IsEnabled = false;
        try
        {
            await _apiClient.ReplaceRoutingAsync(
                _productId,
                _routingSteps.Select(step => (step.Name, (string?)null, step.WorkCenter, step.EstimatedMinutes)).ToList());
            Changed = true;
            RoutingErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            RoutingErrorText.Text = exception.Message;
        }
        finally
        {
            SaveRoutingButton.IsEnabled = true;
        }
    }

    private async void Deactivate_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"Disattivare il prodotto {CodeText.Text}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeactivateProductAsync(_productId);
            Changed = true;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private sealed record BomRow(string MaterialCode, decimal Quantity, string? Notes);

    private sealed record RoutingRow(int Sequence, string Name, string? WorkCenter, decimal EstimatedMinutes);
}
