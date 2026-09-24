using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Full per-serial traceability for one unit: which phases it went through and when (mirrored
/// automatically from the batch-level phase timing — see WorkOrderUnitOperation), and which material
/// lots were attributed to it (best-effort, derived when a withdrawal slip tied to this work order was
/// closed — see WorkOrderUnitMaterialLot). Read-only.</summary>
public partial class WorkOrderUnitDetailWindow : Window
{
    public WorkOrderUnitDetailWindow(ApiClient apiClient, Guid workOrderId, Guid unitId)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                var detail = await apiClient.GetWorkOrderUnitDetailAsync(workOrderId, unitId);
                SerialText.Text = detail.SerialNumber;
                InfoText.Text = $"Esito: {StatusToItalianTextConverter.Translate(detail.Status)} · creata il {detail.CreatedAt:g}" +
                    (detail.ResolvedAt.HasValue ? $" · risolta il {detail.ResolvedAt:g}" : "");
                OperationsList.ItemsSource = detail.Operations;
                MaterialLotsList.ItemsSource = detail.MaterialLots;
            }
            catch (Exception exception)
            {
                ErrorText.Text = exception.Message;
            }
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
