using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

public partial class CreateProductWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid? _editingProductId;
    private IReadOnlyList<LayoutFieldDto> _layout = [];

    public bool Created { get; private set; }

    /// <summary>Creation mode.</summary>
    public CreateProductWindow(ApiClient apiClient) : this(apiClient, existing: null)
    {
    }

    /// <summary>Edit mode: pre-fills nome/descrizione and disables il codice (non modificabile dopo la creazione).</summary>
    public CreateProductWindow(ApiClient apiClient, ProductDetailDto? existing)
    {
        InitializeComponent();
        _apiClient = apiClient;

        if (existing is not null)
        {
            _editingProductId = existing.Id;
            Title = $"Modifica prodotto {existing.Code}";
            TitleText.Text = $"Modifica prodotto {existing.Code}";
            CreateButton.Content = "Salva modifiche";
            CodeBox.Text = existing.Code;
            CodeBox.IsEnabled = false;
            NameBox.Text = existing.Name;
            DescriptionBox.Text = existing.Description;
        }
        else
        {
            Loaded += async (_, _) => await LoadLayoutAsync();
        }
    }

    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "products.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("code", CodeBox),
            new FieldBinding("name", NameBox),
            new FieldBinding("description", DescriptionBox),
        ]);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Il nome prodotto è obbligatorio.";
            return;
        }

        var description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();

        if (_editingProductId is null)
        {
            var missingFields = FormLayoutApplier.Missing(_layout, key => key switch
            {
                "code" => !string.IsNullOrWhiteSpace(CodeBox.Text),
                "name" => !string.IsNullOrWhiteSpace(NameBox.Text),
                "description" => description is not null,
                _ => true,
            });
            if (missingFields.Count > 0)
            {
                ErrorText.Text = "Compila i campi obbligatori: " + string.Join(", ", missingFields) + ".";
                return;
            }
        }

        CreateButton.IsEnabled = false;
        try
        {
            if (_editingProductId is Guid productId)
            {
                await _apiClient.EditProductAsync(productId, name, description);
            }
            else
            {
                var code = CodeBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(code))
                {
                    ErrorText.Text = "Il codice prodotto è obbligatorio.";
                    CreateButton.IsEnabled = true;
                    return;
                }

                await _apiClient.CreateProductAsync(code, name, description);
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
