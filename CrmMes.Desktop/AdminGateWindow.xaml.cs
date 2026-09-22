using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Asks for Admin credentials before letting a not-yet-logged-in user reach server settings
/// from the login screen. It only verifies the credentials (and that the role is Admin) against
/// whichever server is currently configured — it never signs the app into that session, so the real
/// login flow afterward is unaffected.</summary>
public partial class AdminGateWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Verified { get; private set; }

    public AdminGateWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
    }

    private async void Verify_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var email = EmailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(PasswordBox.Password))
        {
            ErrorText.Text = "Inserisci email e password.";
            return;
        }

        VerifyButton.IsEnabled = false;
        try
        {
            var auth = await _apiClient.LoginAsync(email, PasswordBox.Password);
            if (auth.Role != "Admin")
            {
                ErrorText.Text = "Queste credenziali non hanno il ruolo Admin.";
                return;
            }

            Verified = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            VerifyButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
