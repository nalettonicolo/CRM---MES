using System.Windows;

namespace CrmMes.Desktop;

/// <summary>The user's own two-factor: switch it on (QR code, first code, recovery codes shown once), get new
/// recovery codes, switch it off (not when the role requires it). Opened from "Sicurezza account", and at
/// login when the role requires two-factor and it isn't active yet (Mandatory: closing without activating
/// ends the session).</summary>
public partial class TwoFactorWindow : Window
{
    private enum Mode { Loading, Off, Setup, Recovery, On }

    private readonly ApiClient _apiClient;
    private Mode _mode = Mode.Loading;
    private bool _required;

    /// <summary>True once two-factor is active (the caller renews the session to lift the setup limit).</summary>
    public bool Activated { get; private set; }

    public bool Mandatory { get; init; }

    public TwoFactorWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var status = await _apiClient.GetTwoFactorStatusAsync();
            _required = status.Required;
            if (status.Enabled)
            {
                StatusText.Text = $"Attiva. Codici di recupero rimasti: {status.RecoveryCodesLeft}." +
                    (status.Required ? " Per il tuo ruolo è obbligatoria." : string.Empty);
                Show(Mode.On);
            }
            else
            {
                StatusText.Text = Mandatory || status.Required
                    ? "Per il tuo ruolo l'amministratore ha reso obbligatoria la verifica in due passaggi: attivala per continuare."
                    : "Non attiva. È facoltativa, ma consigliata a chi vede costi, fatture o configurazione.";
                Show(Mode.Off);
            }
        }
        catch (InvalidOperationException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Show(Mode mode)
    {
        _mode = mode;
        SetupPanel.Visibility = mode == Mode.Setup ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = mode == Mode.Recovery ? Visibility.Visible : Visibility.Collapsed;
        ManagePanel.Visibility = mode == Mode.On ? Visibility.Visible : Visibility.Collapsed;
        DisablePasswordPanel.Visibility = _required ? Visibility.Collapsed : Visibility.Visible;
        DisableButton.Visibility = mode == Mode.On && !_required ? Visibility.Visible : Visibility.Collapsed;
        NewCodesButton.Visibility = mode == Mode.On ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Visibility = mode is Mode.Off or Mode.Setup or Mode.Recovery ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Content = mode switch
        {
            Mode.Off => "Attiva",
            Mode.Setup => "Conferma codice",
            _ => "Ho salvato i codici",
        };
        CloseButton.Content = Mandatory && !Activated ? "Esci" : "Chiudi";
        CloseButton.Visibility = mode == Mode.Recovery ? Visibility.Collapsed : Visibility.Visible;
        ErrorText.Text = string.Empty;
    }

    private async void Primary_Click(object sender, RoutedEventArgs e)
    {
        PrimaryButton.IsEnabled = false;
        try
        {
            switch (_mode)
            {
                case Mode.Off:
                    var setup = await _apiClient.StartTwoFactorSetupAsync();
                    var png = Convert.FromBase64String(setup.QrCodePng[(setup.QrCodePng.IndexOf(',') + 1)..]);
                    QrImage.Source = QrCodeHelper.ToBitmapImage(png);
                    SecretBox.Text = setup.Secret;
                    Show(Mode.Setup);
                    SetupCodeBox.Focus();
                    break;
                case Mode.Setup:
                    var codes = await _apiClient.EnableTwoFactorAsync(SetupCodeBox.Text.Trim());
                    Activated = true;
                    RecoveryBox.Text = string.Join(Environment.NewLine, codes);
                    StatusText.Text = "Attiva. Da ora a ogni accesso servirà anche il codice dell'app.";
                    Show(Mode.Recovery);
                    break;
                case Mode.Recovery:
                    DialogResult = true;
                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            PrimaryButton.IsEnabled = true;
        }
    }

    private async void NewCodes_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var codes = await _apiClient.NewRecoveryCodesAsync(ManageCodeBox.Text.Trim());
            RecoveryBox.Text = string.Join(Environment.NewLine, codes);
            Show(Mode.Recovery);
        }
        catch (InvalidOperationException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Disable_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Disattivare la verifica in due passaggi? L'account sarà protetto solo dalla password.",
                "Verifica in due passaggi", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DisableTwoFactorAsync(DisablePasswordBox.Password, ManageCodeBox.Text.Trim());
            DialogResult = true;
        }
        catch (InvalidOperationException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void CopyRecovery_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(RecoveryBox.Text);
        }
        catch
        {
            // Clipboard busy: the codes stay selectable in the box.
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = Activated;
}
