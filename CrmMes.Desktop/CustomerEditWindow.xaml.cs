using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Creates a customer, or edits one when an existing <see cref="CustomerDto"/> is passed. The
/// code is the customer's identity on quotes and work orders, so it is locked when editing.</summary>
public partial class CustomerEditWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly CustomerDto? _existing;

    public bool Created { get; private set; }

    public CustomerEditWindow(ApiClient apiClient, CustomerDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _existing = existing;

        if (existing is not null)
        {
            Title = "Modifica cliente";
            HeaderText.Text = "Modifica cliente";
            SaveButton.Content = "Salva";
            NameBox.Text = existing.Name;
            CodeBox.Text = existing.Code;
            CodeBox.IsEnabled = false;
            VatBox.Text = existing.VatNumber ?? string.Empty;
            EmailBox.Text = existing.Email ?? string.Empty;
            PhoneBox.Text = existing.Phone ?? string.Empty;
            AddressBox.Text = existing.Address ?? string.Empty;
            NotesBox.Text = existing.Notes ?? string.Empty;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var code = CodeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            ErrorText.Text = "Ragione sociale e codice sono obbligatori.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveCustomerAsync(_existing?.Id, new SaveCustomerDto(
                name, code, VatBox.Text, EmailBox.Text, PhoneBox.Text, AddressBox.Text, NotesBox.Text));
            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
