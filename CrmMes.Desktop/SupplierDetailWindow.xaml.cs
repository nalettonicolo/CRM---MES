using System.Windows;

namespace CrmMes.Desktop;

public partial class SupplierDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _supplierId;
    private string? _website;

    public bool Changed { get; private set; }

    public SupplierDetailWindow(ApiClient apiClient, Guid supplierId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _supplierId = supplierId;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var detail = await _apiClient.GetSupplierDetailAsync(_supplierId);
            Title = $"Fornitore {detail.Code}";
            SupplierTitleText.Text = detail.Name;
            SupplierInfoText.Text =
                $"Codice {detail.Code} · {(detail.IsActive ? "Attivo" : "Inattivo")}" +
                (string.IsNullOrWhiteSpace(detail.Email) ? "" : $" · {detail.Email}") +
                (string.IsNullOrWhiteSpace(detail.Phone) ? "" : $" · {detail.Phone}") +
                (string.IsNullOrWhiteSpace(detail.Website) ? "" : $" · {detail.Website}");
            _website = detail.Website;
            OpenWebsiteButton.IsEnabled = !string.IsNullOrWhiteSpace(_website);
            PurchaseOrdersList.ItemsSource = detail.PurchaseOrders;
            CatalogList.ItemsSource = detail.CatalogEntries;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_website))
        {
            return;
        }

        try
        {
            var url = _website.Contains("://") ? _website : $"https://{_website}";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Impossibile aprire il link: {exception.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var detail = await _apiClient.GetSupplierDetailAsync(_supplierId);
            var dialog = new CreateSupplierWindow(_apiClient, detail) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.Created)
            {
                Changed = true;
                await ReloadAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
