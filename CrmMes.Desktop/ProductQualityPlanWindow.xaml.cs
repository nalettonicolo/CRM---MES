using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

public partial class ProductQualityPlanWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _productId;

    public ProductQualityPlanWindow(ApiClient apiClient, Guid productId, string productCode)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _productId = productId;
        ProductTitleText.Text = productCode;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            CheckpointsList.ItemsSource = await _apiClient.GetQualityCheckpointsAsync(_productId);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private static decimal? ParseOptionalDecimal(string text) =>
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) ? value : null;

    private async void AddCheckpoint_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Il nome della caratteristica è obbligatorio.";
            return;
        }

        try
        {
            await _apiClient.CreateQualityCheckpointAsync(
                _productId, name,
                string.IsNullOrWhiteSpace(UnitBox.Text) ? null : UnitBox.Text.Trim(),
                ParseOptionalDecimal(NominalBox.Text), ParseOptionalDecimal(LowerBox.Text), ParseOptionalDecimal(UpperBox.Text));

            NameBox.Text = string.Empty;
            UnitBox.Text = string.Empty;
            NominalBox.Text = string.Empty;
            LowerBox.Text = string.Empty;
            UpperBox.Text = string.Empty;
            ErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void RemoveCheckpoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: QualityCheckpointDto checkpoint })
        {
            return;
        }

        try
        {
            await _apiClient.DeactivateQualityCheckpointAsync(checkpoint.Id);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
