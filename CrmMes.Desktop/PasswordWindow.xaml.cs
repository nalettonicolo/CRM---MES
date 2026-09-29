using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Two uses: the signed-in user changes their own password (current one required), or an Admin
/// resets another user's password. Either way the API ends that user's sessions on other PCs.</summary>
public partial class PasswordWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly UserRowDto? _resetFor;

    public bool Saved { get; private set; }

    /// <param name="resetFor">Null = change my own password; otherwise the user an Admin is resetting.</param>
    public PasswordWindow(ApiClient apiClient, UserRowDto? resetFor = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _resetFor = resetFor;

        if (resetFor is null)
        {
            Title = HeaderText.Text = "Cambia password";
            InfoText.Text = "Dopo il cambio, le sessioni aperte con questo account su altri PC terminano.";
        }
        else
        {
            Title = HeaderText.Text = $"Reimposta password · {resetFor.Name}";
            InfoText.Text = "Imposta una nuova password per questo utente, ad esempio se l'ha dimenticata o se può essere stata esposta. " +
                            "Sblocca anche l'account se era bloccato dopo troppi tentativi, e chiude le sue sessioni aperte.";
            CurrentPanel.Visibility = Visibility.Collapsed;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (NewBox.Password.Length < 8)
        {
            ErrorText.Text = "La nuova password deve avere almeno 8 caratteri.";
            return;
        }

        if (NewBox.Password != ConfirmBox.Password)
        {
            ErrorText.Text = "Le due password non coincidono.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            if (_resetFor is null)
            {
                await _apiClient.ChangePasswordAsync(CurrentBox.Password, NewBox.Password);
            }
            else
            {
                await _apiClient.ResetUserPasswordAsync(_resetFor.Id, NewBox.Password);
            }

            Saved = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
