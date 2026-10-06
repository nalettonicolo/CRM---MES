using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

public partial class CreateMaterialWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    private IReadOnlyList<LayoutFieldDto> _layout = [];

    public CreateMaterialWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadLayoutAsync();
    }

    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "materials.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("code", CodeBox),
            new FieldBinding("name", NameBox),
            new FieldBinding("unit", UnitBox),
            new FieldBinding("stock", StockBox),
            new FieldBinding("minStock", MinStockBox),
        ]);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        var name = NameBox.Text.Trim();
        var unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? "pz" : UnitBox.Text.Trim();

        var missing = FormLayoutApplier.Missing(_layout, key => key switch
        {
            "code" => !string.IsNullOrWhiteSpace(code),
            "name" => !string.IsNullOrWhiteSpace(name),
            "unit" => !string.IsNullOrWhiteSpace(UnitBox.Text),
            "stock" => !string.IsNullOrWhiteSpace(StockBox.Text),
            "minStock" => !string.IsNullOrWhiteSpace(MinStockBox.Text),
            _ => true,
        });
        if (missing.Count > 0)
        {
            ErrorText.Text = "Compila i campi obbligatori: " + string.Join(", ", missing) + ".";
            return;
        }

        if (!NumberInput.TryParseDecimal(StockBox.Text, out var stock) || stock < 0 ||
            !NumberInput.TryParseDecimal(MinStockBox.Text, out var minStock) || minStock < 0)
        {
            ErrorText.Text = "Giacenza e scorta minima devono essere numeri non negativi.";
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            await _apiClient.CreateMaterialAsync(code, name, unit, stock, minStock);
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
