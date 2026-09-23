using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateSupplierWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _editingSupplierId;

    public bool Created { get; private set; }

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

        CreateButton.IsEnabled = false;
        try
        {
            if (_editingSupplierId is Guid supplierId)
            {
                await _apiClient.EditSupplierAsync(supplierId, name, EmailBox.Text, PhoneBox.Text, WebsiteBox.Text);
            }
            else
            {
                await _apiClient.CreateSupplierAsync(name, code, EmailBox.Text, PhoneBox.Text, WebsiteBox.Text);
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
