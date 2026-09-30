using System.Windows;
using System.Windows.Input;

namespace CrmMes.Desktop;

/// <summary>Second step of a login with two-factor: the code from the app (or a recovery code).</summary>
public partial class TwoFactorCodeWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly string _challenge;

    public AuthDto? Result { get; private set; }

    public TwoFactorCodeWindow(ApiClient apiClient, string challenge)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _challenge = challenge;
        Loaded += (_, _) => CodeBox.Focus();
    }

    private void CodeBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Confirm_Click(sender, e);
        }
    }

    private async void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CodeBox.Text))
        {
            ErrorText.Text = "Inserisci il codice.";
            return;
        }

        ConfirmButton.IsEnabled = false;
        ErrorText.Text = string.Empty;
        try
        {
            Result = await _apiClient.LoginTwoFactorAsync(_challenge, CodeBox.Text.Trim());
            DialogResult = true;
        }
        catch (InvalidOperationException exception)
        {
            ErrorText.Text = exception.Message;
            CodeBox.SelectAll();
            CodeBox.Focus();
        }
        finally
        {
            ConfirmButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
