using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateMaterialLotWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    public CreateMaterialLotWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var materialCode = MaterialCodeBox.Text.Trim();
        var lotNumber = LotNumberBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(materialCode) || string.IsNullOrWhiteSpace(lotNumber) ||
            !decimal.TryParse(QuantityBox.Text, out var quantity) || quantity <= 0)
        {
            ErrorText.Text = "Codice materiale, numero lotto e quantità (maggiore di zero) sono obbligatori.";
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            await _apiClient.CreateMaterialLotAsync(materialCode, lotNumber, quantity, string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim());
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
