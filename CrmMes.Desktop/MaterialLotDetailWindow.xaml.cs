using System.Windows;

namespace CrmMes.Desktop;

public partial class MaterialLotDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _lotId;

    public MaterialLotDetailWindow(ApiClient apiClient, Guid lotId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _lotId = lotId;
        Loaded += MaterialLotDetailWindow_Loaded;
    }

    private async void MaterialLotDetailWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var lot = await _apiClient.GetMaterialLotAsync(_lotId);

            TitleText.Text = $"{lot.MaterialCode} · lotto {lot.LotNumber}";
            QuantityText.Text = lot.Quantity.ToString();
            InitialQuantityText.Text = lot.InitialQuantity.ToString();
            ReceivedAtText.Text = lot.ReceivedAt.ToLocalTime().ToString("g");
            NotesText.Text = string.IsNullOrWhiteSpace(lot.Notes) ? string.Empty : lot.Notes;

            UsagesList.ItemsSource = lot.Usages;

            LoadingText.Visibility = Visibility.Collapsed;
            HeaderInfo.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            LoadingText.Text = $"Impossibile caricare il lotto: {exception.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
