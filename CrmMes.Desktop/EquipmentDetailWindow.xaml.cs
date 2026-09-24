using System.Windows;

namespace CrmMes.Desktop;

public partial class EquipmentDetailWindow : Window
{
    public EquipmentDetailWindow(ApiClient apiClient, Guid equipmentId)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                var detail = await apiClient.GetEquipmentDetailAsync(equipmentId);
                Title = $"Macchina {detail.Code}";
                EquipmentTitleText.Text = detail.Name;
                EquipmentInfoText.Text =
                    $"Codice {detail.Code} · {(detail.IsActive ? "Attiva" : "Inattiva")}" +
                    (string.IsNullOrWhiteSpace(detail.WorkCenterName) ? "" : $" · Centro di lavoro: {detail.WorkCenterName}");
                TasksList.ItemsSource = detail.Tasks;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }
}
