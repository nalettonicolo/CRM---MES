using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

public partial class CreateSupplierWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _editingSupplierId;
    private readonly IReadOnlyDictionary<string, string?> _existingCustomFields;

    public bool Created { get; private set; }

    private IReadOnlyList<LayoutFieldDto> _layout = [];
    private IReadOnlyList<CustomFieldDefinitionDto> _customFieldDefinitions = [];
    private List<CustomFieldControl> _customFieldControls = [];

    /// <summary>Creation mode.</summary>
    public CreateSupplierWindow(ApiClient apiClient) : this(apiClient, existing: null)
    {
    }

    /// <summary>Edit mode: pre-fills i campi modificabili; il codice non è modificabile dopo la
    /// creazione (identifica il fornitore in cataloghi/ordini già registrati).</summary>
    public CreateSupplierWindow(ApiClient apiClient, SupplierDetailDto? existing)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _existingCustomFields = existing?.CustomFields ?? new Dictionary<string, string?>();

        if (existing is not null)
        {
            _editingSupplierId = existing.Id;
            Title = $"Modifica fornitore {existing.Code}";
            TitleText.Text = $"Modifica fornitore {existing.Code}";
            CreateButton.Content = "Salva modifiche";
            NameBox.Text = existing.Name;
            CodeBox.Text = existing.Code;
            CodeBox.IsEnabled = false;
            EmailBox.Text = existing.Email;
            PhoneBox.Text = existing.Phone;
            WebsiteBox.Text = existing.Website;
        }

        Loaded += async (_, _) =>
        {
            if (existing is null)
            {
                await LoadLayoutAsync();
            }

            await LoadCustomFieldsAsync();
        };
    }

    /// <summary>Layout del modulo fornitore in creazione: etichette, visibilità e obbligatorietà dall'Admin.
    /// Nome e codice restano sempre obbligatori: il server li richiede comunque.</summary>
    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "suppliers.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("name", NameBox),
            new FieldBinding("code", CodeBox),
            new FieldBinding("email", EmailBox),
            new FieldBinding("phone", PhoneBox),
            new FieldBinding("website", WebsiteBox),
        ]);
    }

    /// <summary>Campi personalizzati aggiunti dall'Admin alla schermata "Nuovo fornitore" (es. "Giorni di
    /// pagamento"): in modifica arrivano precompilati con il valore già salvato per questo fornitore.</summary>
    private async Task LoadCustomFieldsAsync()
    {
        _customFieldDefinitions = await CustomFieldForm.LoadAsync(_apiClient, "suppliers.new");
        _customFieldControls = CustomFieldForm.BuildControls(CustomFieldsPanel, _customFieldDefinitions, _existingCustomFields);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var code = CodeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            ErrorText.Text = "Nome e codice sono obbligatori.";
            return;
        }

        var customFieldValues = CustomFieldForm.ToValues(_customFieldControls);

        if (_editingSupplierId is null)
        {
            var missing = FormLayoutApplier.Missing(_layout, key => key switch
            {
                "name" => true,
                "code" => true,
                "email" => !string.IsNullOrWhiteSpace(EmailBox.Text),
                "phone" => !string.IsNullOrWhiteSpace(PhoneBox.Text),
                "website" => !string.IsNullOrWhiteSpace(WebsiteBox.Text),
                _ => true,
            });
            missing.AddRange(CustomFieldForm.Missing(_customFieldDefinitions, customFieldValues));
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

        CreateButton.IsEnabled = false;
        try
        {
            if (_editingSupplierId is Guid supplierId)
            {
                await _apiClient.EditSupplierAsync(supplierId, name, EmailBox.Text, PhoneBox.Text, WebsiteBox.Text, customFieldValues);
            }
            else
            {
                await _apiClient.CreateSupplierAsync(name, code, EmailBox.Text, PhoneBox.Text, WebsiteBox.Text, customFieldValues);
            }

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
