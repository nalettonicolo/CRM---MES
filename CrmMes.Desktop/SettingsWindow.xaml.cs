using System.Windows;
using System.Windows.Media;

namespace CrmMes.Desktop;

public partial class SettingsWindow : Window
{
    private readonly ApiClient _apiClient;

    /// <summary>True se l'utente ha salvato un nuovo indirizzo: il chiamante deve rieseguire la connessione.</summary>
    public bool SettingsChanged { get; private set; }

    public SettingsWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        ApiUrlBox.Text = ClientSettings.Load().ApiBaseUrl;
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                ApiUrlBox.Text = Clipboard.GetText().Trim();
                ApiUrlBox.CaretIndex = ApiUrlBox.Text.Length;
            }
        }
        catch
        {
            // Appunti non accessibili (raro, es. bloccati da un'altra app): l'utente può comunque digitare a mano.
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ApiUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            SetResult("Indirizzo non valido.", success: false);
            return;
        }

        TestButton.IsEnabled = false;
        SetResult("Verifica in corso...", success: true);
        try
        {
            var probe = new ApiClient();
            probe.SetBaseUrl(url);
            var healthy = await probe.IsHealthyAsync();
            SetResult(healthy ? "Connessione riuscita." : "Il server ha risposto ma non è integro.", success: healthy);
        }
        catch
        {
            SetResult("Impossibile raggiungere questo indirizzo.", success: false);
        }
        finally
        {
            TestButton.IsEnabled = true;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var url = ApiUrlBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            SetResult("Indirizzo non valido.", success: false);
            return;
        }

        var settings = new ClientSettings { ApiBaseUrl = url };
        settings.Save();
        _apiClient.SetBaseUrl(url);
        SettingsChanged = true;
        Close();
    }

    private void SetResult(string text, bool success)
    {
        TestResultText.Text = text;
        TestResultText.Foreground = success
            ? (Brush)FindResource("SuccessTextBrush")
            : (Brush)FindResource("DangerBrush");
    }
}
