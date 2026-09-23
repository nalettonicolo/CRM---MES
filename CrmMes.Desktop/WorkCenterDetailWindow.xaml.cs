using System.Windows;

namespace CrmMes.Desktop;

public partial class WorkCenterDetailWindow : Window
{
    public WorkCenterDetailWindow(ApiClient apiClient, Guid workCenterId)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                var detail = await apiClient.GetWorkCenterDetailAsync(workCenterId);
                Title = $"Centro di lavoro {detail.Code}";
                CenterTitleText.Text = detail.Name;
                CenterInfoText.Text =
                    $"Codice {detail.Code} · Capacità {detail.DailyCapacityMinutes} min/giorno · {(detail.IsActive ? "Attivo" : "Inattivo")}" +
                    (string.IsNullOrWhiteSpace(detail.Description) ? "" : $" · {detail.Description}");
                OperationsList.ItemsSource = detail.PendingOperations;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }
}
