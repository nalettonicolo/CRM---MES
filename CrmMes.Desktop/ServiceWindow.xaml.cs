using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Service post-vendita: macchine installate presso i clienti e le richieste di assistenza che
/// arrivano su di esse, con i relativi interventi. Creare macchine e richieste è riservato ad Admin,
/// Direzione e Commerciale (la stessa regola dell'API); registrare un intervento è aperto a chiunque sia
/// autenticato, come il resto del lavoro di reparto.</summary>
public partial class ServiceWindow : Window
{
    private readonly ApiClient _apiClient;
    private bool _canManage;
    private ServiceRequestDetailDto? _selectedRequest;

    private sealed class RequestRow(ServiceRequestRowDto source)
    {
        public Guid Id { get; } = source.Id;
        public string NumberLabel { get; } = $"RA {source.Number}";
        public string CustomerName { get; } = source.CustomerName;
        public string MachineLabel { get; } = $"{source.MachineName} ({source.SerialNumber})";
        public string Subject { get; } = source.Subject;
        public string PriorityLabel { get; } = source.Priority == "Urgente" ? "Urgente" : "Normale";
        public string OpenedLabel { get; } = source.OpenedAt.ToLocalTime().ToString("dd/MM/yyyy");
        public string StatusLabel { get; } = source.Status switch { "Open" => "Aperta", "InProgress" => "In lavorazione", "Closed" => "Chiusa", _ => source.Status };
    }

    private sealed class MachineRow(InstalledMachineDto source)
    {
        public string Name { get; } = source.Name;
        public string SerialNumber { get; } = source.SerialNumber;
        public string CustomerName { get; } = source.CustomerName;
        public string Location { get; } = source.Location ?? "—";
        public int OpenRequestCount { get; } = source.OpenRequestCount;
        public string WarrantyLabel { get; } = Describe(source.WarrantyUntil);

        private static string Describe(DateTime? warrantyUntil)
        {
            if (warrantyUntil is not { } until)
            {
                return "—";
            }

            var days = (until.Date - DateTime.Today).Days;
            var date = until.ToString("dd/MM/yyyy");
            return days < 0 ? $"{date} (scaduta)" : days <= 30 ? $"{date} (in scadenza, {days} gg)" : date;
        }
    }

    public ServiceWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _canManage = apiClient.CurrentRole is "Admin" or "Management" or "Sales";
        NewMachinePanel.Visibility = _canManage ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await LoadAllAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAllAsync();

    private async Task LoadAllAsync()
    {
        ErrorText.Text = string.Empty;
        await LoadRequestsAsync();
        await LoadMachinesAsync();
    }

    private async Task LoadRequestsAsync()
    {
        try
        {
            var status = (RequestStatusFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
            var requests = await _apiClient.GetServiceRequestsAsync(status);
            RequestsList.ItemsSource = requests.Select(r => new RequestRow(r)).ToList();
            RequestDetailPanel.Visibility = Visibility.Collapsed;
            _selectedRequest = null;
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async Task LoadMachinesAsync()
    {
        try
        {
            var machines = await _apiClient.GetInstalledMachinesAsync();
            MachinesList.ItemsSource = machines.Select(m => new MachineRow(m)).ToList();
            var expiring = machines.Count(m => m.WarrantyUntil is { } until && (until.Date - DateTime.Today).Days is >= 0 and <= 30);
            if (expiring > 0)
            {
                WarrantyNoticeText.Text = $"{expiring} {(expiring == 1 ? "macchina ha" : "macchine hanno")} la garanzia in scadenza entro 30 giorni.";
                WarrantyNoticeText.Visibility = Visibility.Visible;
            }
            else
            {
                WarrantyNoticeText.Visibility = Visibility.Collapsed;
            }

            if (_canManage)
            {
                var customers = await _apiClient.GetCustomersAsync();
                NewMachineCustomerCombo.ItemsSource = customers;
            }
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void RequestStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => await LoadRequestsAsync();

    private async void RequestsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RequestsList.SelectedItem is not RequestRow row)
        {
            return;
        }

        try
        {
            _selectedRequest = await _apiClient.GetServiceRequestAsync(row.Id);
            var interventions = string.Join("\n", _selectedRequest.Interventions.Select(i =>
                $"{i.ScheduledAt.ToLocalTime():dd/MM/yyyy}: {i.TechnicianName ?? "—"} — {i.Description ?? "(nessuna descrizione)"} ({(i.InWarranty ? "in garanzia" : "fuori garanzia")}{(i.Hours is null ? "" : $", {i.Hours} h")})"));
            RequestDetailText.Text = $"{row.NumberLabel} · {_selectedRequest.Subject} · {row.StatusLabel}\n{_selectedRequest.Description}\n\nInterventi:\n{(interventions.Length == 0 ? "nessuno ancora." : interventions)}";
            CloseRequestButton.Visibility = _canManage && _selectedRequest.Status != "Closed" ? Visibility.Visible : Visibility.Collapsed;
            ReopenRequestButton.Visibility = _canManage && _selectedRequest.Status == "Closed" ? Visibility.Visible : Visibility.Collapsed;
            AddInterventionButton.IsEnabled = _selectedRequest.Status != "Closed";
            RequestDetailPanel.Visibility = Visibility.Visible;
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void AddIntervention_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRequest is null)
        {
            return;
        }

        decimal? hours = decimal.TryParse(HoursBox.Text, out var h) ? h : null;
        try
        {
            ErrorText.Text = string.Empty;
            await _apiClient.AddServiceInterventionAsync(_selectedRequest.Id, TechnicianBox.Text, CompletedBox.IsChecked == true, hours,
                InWarrantyBox.IsChecked == true, InterventionDescriptionBox.Text, null);
            TechnicianBox.Text = string.Empty;
            HoursBox.Text = string.Empty;
            InterventionDescriptionBox.Text = string.Empty;
            InWarrantyBox.IsChecked = false;
            CompletedBox.IsChecked = false;
            await LoadRequestsAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void CloseRequest_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRequest is null)
        {
            return;
        }

        try
        {
            await _apiClient.CloseServiceRequestAsync(_selectedRequest.Id);
            await LoadRequestsAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void ReopenRequest_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRequest is null)
        {
            return;
        }

        try
        {
            await _apiClient.ReopenServiceRequestAsync(_selectedRequest.Id);
            await LoadRequestsAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void CreateMachine_Click(object sender, RoutedEventArgs e)
    {
        if (NewMachineCustomerCombo.SelectedItem is not CustomerDto customer || string.IsNullOrWhiteSpace(NewMachineNameBox.Text) || string.IsNullOrWhiteSpace(NewMachineSerialBox.Text))
        {
            ErrorText.Text = "Scegli il cliente e scrivi nome e matricola della macchina.";
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            await _apiClient.CreateInstalledMachineAsync(customer.Id, NewMachineNameBox.Text, NewMachineModelBox.Text, NewMachineSerialBox.Text,
                NewMachineLocationBox.Text, null, NewMachineWarrantyPicker.SelectedDate);
            NewMachineNameBox.Text = string.Empty;
            NewMachineModelBox.Text = string.Empty;
            NewMachineSerialBox.Text = string.Empty;
            NewMachineLocationBox.Text = string.Empty;
            NewMachineWarrantyPicker.SelectedDate = null;
            await LoadMachinesAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }
}
