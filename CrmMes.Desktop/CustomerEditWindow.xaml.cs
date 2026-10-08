using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

/// <summary>Creates a customer, or edits one when an existing <see cref="CustomerDto"/> is passed. The
/// code is the customer's identity on quotes and work orders, so it is locked when editing.</summary>
public partial class CustomerEditWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly CustomerDto? _existing;
    private readonly IReadOnlyDictionary<string, string?> _existingCustomFields;

    private IReadOnlyList<CustomFieldDefinitionDto> _customFieldDefinitions = [];
    private List<CustomFieldControl> _customFieldControls = [];

    public bool Created { get; private set; }

    public CustomerEditWindow(ApiClient apiClient, CustomerDto? existing = null, IReadOnlyDictionary<string, string?>? existingCustomFields = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _existing = existing;
        _existingCustomFields = existingCustomFields ?? new Dictionary<string, string?>();

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

        Loaded += async (_, _) => await LoadCustomFieldsAsync();
    }

    /// <summary>Campi personalizzati aggiunti dall'Admin alla schermata "Nuovo cliente" (es. "Giorni di
    /// pagamento"): in modifica arrivano precompilati con il valore già salvato per questo cliente.</summary>
    private async Task LoadCustomFieldsAsync()
    {
        _customFieldDefinitions = await CustomFieldForm.LoadAsync(_apiClient, "customers.new");
        _customFieldControls = CustomFieldForm.BuildControls(CustomFieldsPanel, _customFieldDefinitions, _existingCustomFields);
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

        var customFieldValues = CustomFieldForm.ToValues(_customFieldControls);

        if (_existing is null)
        {
            var missing = CustomFieldForm.Missing(_customFieldDefinitions, customFieldValues);
            if (missing.Count > 0)
            {
                ErrorText.Text = "Compila i campi obbligatori: " + string.Join(", ", missing) + ".";
                return;
            }
        }

        var typeError = CustomFieldForm.Validate(_customFieldDefinitions, customFieldValues);
        if (typeError is not null)
        {
            ErrorText.Text = typeError;
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveCustomerAsync(_existing?.Id, new SaveCustomerDto(
                name, code, VatBox.Text, EmailBox.Text, PhoneBox.Text, AddressBox.Text, NotesBox.Text, customFieldValues));
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
