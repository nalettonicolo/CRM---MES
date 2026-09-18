using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateMaterialWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    public CreateMaterialWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var code = CodeBox.Text.Trim();
        var name = NameBox.Text.Trim();
        var unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? "pz" : UnitBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Codice e descrizione sono obbligatori.";
            return;
        }

        if (!decimal.TryParse(StockBox.Text, out var stock) || stock < 0 ||
            !decimal.TryParse(MinStockBox.Text, out var minStock) || minStock < 0)
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
