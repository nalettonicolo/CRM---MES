using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Label data of a product (food module): legal name, shelf life, date mark type, storage and
/// net quantity. Ingredients and allergens come from its bill of materials.</summary>
public partial class ProductFoodInfoWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _productId;

    public ProductFoodInfoWindow(ApiClient apiClient, Guid productId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _productId = productId;
        Loaded += async (_, _) =>
        {
            try
            {
                var info = await _apiClient.GetProductFoodInfoAsync(_productId);
                HeaderText.Text = $"{info.Code} · {info.Name}";
                SalesNameBox.Text = info.SalesName ?? string.Empty;
                ShelfLifeBox.Text = info.ShelfLifeDays?.ToString() ?? string.Empty;
                UseByCheck.IsChecked = info.UseByDate;
                StorageBox.Text = info.StorageConditions ?? string.Empty;
                NetQuantityBox.Text = info.NetQuantity ?? string.Empty;
                SaveButton.IsEnabled = true;
            }
            catch (Exception exception)
            {
                ErrorText.Text = exception.Message;
            }
        };
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        int? shelfLife = null;
        if (!string.IsNullOrWhiteSpace(ShelfLifeBox.Text))
        {
            if (!int.TryParse(ShelfLifeBox.Text.Trim(), out var days) || days < 1)
            {
                ErrorText.Text = "Durata non valida (giorni, almeno 1).";
                return;
            }

            shelfLife = days;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveProductFoodInfoAsync(_productId, new SaveProductFoodInfoDto(
                Clean(SalesNameBox.Text), shelfLife, UseByCheck.IsChecked == true, Clean(StorageBox.Text), Clean(NetQuantityBox.Text)));
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
