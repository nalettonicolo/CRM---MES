using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateCarrierWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    public CreateCarrierWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
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
            await _apiClient.CreateCarrierAsync(name, code, EmailBox.Text, PhoneBox.Text);
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
