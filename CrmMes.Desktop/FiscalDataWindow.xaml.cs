using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Tax data needed by electronic invoices: of a customer (tax code, SDI code or PEC, address in
/// structured form) or of the company itself (tax regime, address, REA, IBAN).</summary>
public partial class FiscalDataWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _customerId;

    private FiscalDataWindow(ApiClient apiClient, Guid? customerId, string header)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _customerId = customerId;
        HeaderText.Text = header;
        var company = customerId is null;
        SdiPanel.Visibility = PecPanel.Visibility = company ? Visibility.Collapsed : Visibility.Visible;
        RegimePanel.Visibility = CompanyExtra.Visibility = company ? Visibility.Visible : Visibility.Collapsed;
        CountryBox.IsEnabled = !company;
        HintText.Text = company
            ? "Dati del cedente stampati in ogni fattura elettronica. La partita IVA è quella dei dati azienda."
            : "Senza codice SDI né PEC la fattura va comunque allo SdI e il cliente la trova nel suo cassetto fiscale. Per un cliente estero indica la nazione (es. DE): il codice diventa XXXXXXX.";
        Loaded += async (_, _) => await LoadAsync();
    }

    public static FiscalDataWindow ForCustomer(ApiClient apiClient, CustomerDto customer) =>
        new(apiClient, customer.Id, customer.Name);

    public static FiscalDataWindow ForCompany(ApiClient apiClient) =>
        new(apiClient, null, apiClient.CompanyProfile?.CompanyName is { Length: > 0 } name ? name : "La tua azienda");

    private async Task LoadAsync()
    {
        try
        {
            if (_customerId is { } id)
            {
                var data = await _apiClient.GetCustomerFiscalAsync(id);
                FiscalCodeBox.Text = data.FiscalCode ?? string.Empty;
                SdiBox.Text = data.SdiCode ?? string.Empty;
                PecBox.Text = data.Pec ?? string.Empty;
                Fill(data.Street, data.PostalCode, data.City, data.Province, data.Country);
            }
            else
            {
                var data = await _apiClient.GetCompanyFiscalAsync();
                FiscalCodeBox.Text = data.FiscalCode ?? string.Empty;
                RegimeCombo.SelectedValue = data.TaxRegime ?? "RF01";
                ReaOfficeBox.Text = data.ReaOffice ?? string.Empty;
                ReaNumberBox.Text = data.ReaNumber ?? string.Empty;
                IbanBox.Text = data.Iban ?? string.Empty;
                Fill(data.Street, data.PostalCode, data.City, data.Province, data.Country);
            }

            SaveButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Fill(string? street, string? cap, string? city, string? province, string? country)
    {
        StreetBox.Text = street ?? string.Empty;
        CapBox.Text = cap ?? string.Empty;
        CityBox.Text = city ?? string.Empty;
        ProvinceBox.Text = province ?? string.Empty;
        CountryBox.Text = country ?? "IT";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        SaveButton.IsEnabled = false;
        try
        {
            if (_customerId is { } id)
            {
                await _apiClient.SaveCustomerFiscalAsync(id, new CustomerFiscalDto(
                    Clean(FiscalCodeBox.Text), Clean(SdiBox.Text), Clean(PecBox.Text), Clean(StreetBox.Text), Clean(CapBox.Text),
                    Clean(CityBox.Text), Clean(ProvinceBox.Text), Clean(CountryBox.Text) ?? "IT"));
            }
            else
            {
                await _apiClient.SaveCompanyFiscalAsync(new CompanyFiscalDto(
                    Clean(FiscalCodeBox.Text), RegimeCombo.SelectedValue as string ?? "RF01", Clean(StreetBox.Text), Clean(CapBox.Text),
                    Clean(CityBox.Text), Clean(ProvinceBox.Text), "IT", Clean(ReaOfficeBox.Text), Clean(ReaNumberBox.Text), Clean(IbanBox.Text)));
            }

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
