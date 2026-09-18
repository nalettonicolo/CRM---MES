using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

public partial class CreateUserWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    public CreateUserWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = PasswordBox.Password;
        var role = (RoleCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Operator";

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email))
        {
            ErrorText.Text = "Nome ed email sono obbligatori.";
            return;
        }

        if (password.Length < 8)
        {
            ErrorText.Text = "La password deve avere almeno 8 caratteri.";
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            await _apiClient.CreateUserAsync(name, email, password, role);
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
