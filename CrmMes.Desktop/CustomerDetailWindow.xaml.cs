using System.Windows;

namespace CrmMes.Desktop;

/// <summary>"Cosa ha, cosa vede" a customer: registry data, its quotes and the work orders built for it,
/// loaded with one API call.</summary>
public partial class CustomerDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _customerId;
    private CustomerDto? _customer;
    private Dictionary<string, string?> _customFields = [];

    /// <summary>True when the customer was edited from here, so the list behind can refresh.</summary>
    public bool Changed { get; private set; }

    public CustomerDetailWindow(ApiClient apiClient, Guid customerId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _customerId = customerId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var detail = await _apiClient.GetCustomerDetailAsync(_customerId);
            _customer = detail.Customer;
            _customFields = detail.CustomFields ?? [];
            Title = $"Cliente {detail.Customer.Code}";
            CustomerTitleText.Text = detail.Customer.Name;
            CustomerInfoText.Text = string.Join(" · ", new[]
            {
                $"Codice {detail.Customer.Code}",
                detail.Customer.IsActive ? "Attivo" : "Inattivo",
                string.IsNullOrWhiteSpace(detail.Customer.VatNumber) ? null : $"P.IVA {detail.Customer.VatNumber}",
                detail.Customer.Email,
                detail.Customer.Phone,
                detail.Customer.Address
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
            QuotesList.ItemsSource = detail.Quotes;
            WorkOrdersList.ItemsSource = detail.WorkOrders;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_customer is null)
        {
            return;
        }

        var dialog = new CustomerEditWindow(_apiClient, _customer, _customFields) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            Changed = true;
            await LoadAsync();
        }
    }
}
