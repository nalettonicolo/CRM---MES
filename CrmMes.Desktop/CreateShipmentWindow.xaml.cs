using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateShipmentWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    public CreateShipmentWindow(
        ApiClient apiClient,
        IReadOnlyList<CarrierDto> carriers,
        IReadOnlyList<PurchaseOrderSummaryDto> purchaseOrders,
        IReadOnlyList<WorkOrderSummaryDto> workOrders)
    {
        InitializeComponent();
        _apiClient = apiClient;
        CarrierCombo.ItemsSource = carriers;
        PurchaseOrderCombo.ItemsSource = purchaseOrders;
        WorkOrderCombo.ItemsSource = workOrders;
    }

    private void Direction_Changed(object sender, RoutedEventArgs e)
    {
        if (PurchaseOrderPanel is null || WorkOrderPanel is null)
        {
            return;
        }

        var isInbound = InboundRadio.IsChecked == true;
        PurchaseOrderPanel.Visibility = isInbound ? Visibility.Visible : Visibility.Collapsed;
        WorkOrderPanel.Visibility = isInbound ? Visibility.Collapsed : Visibility.Visible;
        if (isInbound)
        {
            WorkOrderCombo.SelectedItem = null;
        }
        else
        {
            PurchaseOrderCombo.SelectedItem = null;
        }
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (CarrierCombo.SelectedItem is not CarrierDto carrier)
        {
            ErrorText.Text = "Seleziona un corriere.";
            return;
        }

        var direction = InboundRadio.IsChecked == true ? "Inbound" : "Outbound";
        var purchaseOrderId = direction == "Inbound" ? (PurchaseOrderCombo.SelectedItem as PurchaseOrderSummaryDto)?.Id : null;
        var workOrderId = direction == "Outbound" ? (WorkOrderCombo.SelectedItem as WorkOrderSummaryDto)?.Id : null;
        var trackingNumber = string.IsNullOrWhiteSpace(TrackingNumberBox.Text) ? null : TrackingNumberBox.Text.Trim();
        var counterpartReference = string.IsNullOrWhiteSpace(CounterpartReferenceBox.Text) ? null : CounterpartReferenceBox.Text.Trim();
        var address = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim();
        var notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim();

        CreateButton.IsEnabled = false;
        try
        {
            await _apiClient.CreateShipmentAsync(
                direction, carrier.Id, trackingNumber, purchaseOrderId, workOrderId,
                counterpartReference, address, notes, ExpectedAtPicker.SelectedDate);
            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
