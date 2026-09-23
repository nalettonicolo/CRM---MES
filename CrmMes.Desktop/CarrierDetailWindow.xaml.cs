using System.Windows;

namespace CrmMes.Desktop;

public partial class CarrierDetailWindow : Window
{
    public CarrierDetailWindow(ApiClient apiClient, Guid carrierId)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                var detail = await apiClient.GetCarrierDetailAsync(carrierId);
                Title = $"Corriere {detail.Code}";
                CarrierTitleText.Text = detail.Name;
                CarrierInfoText.Text =
                    $"Codice {detail.Code} · {(detail.IsActive ? "Attivo" : "Inattivo")}" +
                    (string.IsNullOrWhiteSpace(detail.Email) ? "" : $" · {detail.Email}") +
                    (string.IsNullOrWhiteSpace(detail.Phone) ? "" : $" · {detail.Phone}");
                ShipmentsList.ItemsSource = detail.Shipments;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }
}
