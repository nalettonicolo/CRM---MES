using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Teleassistenza: who to call, a RustDesk remote session, the diagnostic package. Works from the
/// login screen too (no credentials needed): that's when help is needed most.</summary>
public partial class SupportWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly int _offlineQueueCount;
    private SupportInfoDto? _info;
    private bool _reachable;

    public SupportWindow(ApiClient apiClient, int offlineQueueCount = 0)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _offlineQueueCount = offlineQueueCount;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _info = await _apiClient.GetSupportInfoAsync();
            _reachable = true;
        }
        catch (Exception exception)
        {
            ClientLog.Error("Teleassistenza: contatti non disponibili", exception);
            _reachable = false;
        }

        var contacts = new[]
        {
            _info?.Name,
            _info?.Phone is { } phone ? $"Telefono: {phone}" : null,
            _info?.Email is { } email ? $"Email: {email}" : null,
            _info?.Hours is { } hours ? $"Orari: {hours}" : null,
        }.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        ContactsText.Text = contacts.Count > 0
            ? string.Join(Environment.NewLine, contacts)
            : _reachable
                ? "Contatti non ancora impostati: chiedi all'amministratore chi presta assistenza."
                : "Server non raggiungibile: contatta direttamente chi presta assistenza e crea il pacchetto diagnostico.";

        if (!string.IsNullOrWhiteSpace(_info?.RustDeskIdServer))
        {
            RustDeskServerPanel.Visibility = Visibility.Visible;
            RustDeskServerBox.Text = _info.RustDeskIdServer;
            RustDeskKeyBox.Text = _info.RustDeskKey ?? string.Empty;
        }

        RemoteButton.Content = RustDesk.Find() is null ? "Scarica RustDesk" : "Avvia assistenza remota";
        InfoText.Text = InfoLines();
    }

    private string InfoLines() => string.Join(Environment.NewLine,
        $"Programma:  {SupportPackage.ClientVersion}",
        $"Server:     {_apiClient.BaseAddress} ({(_reachable ? "raggiungibile" : "non raggiungibile")})",
        $"Versione server: {_info?.ServerVersion ?? "—"} · {HostingName(_info?.Hosting)}",
        $"PC:         {Environment.MachineName}",
        $"Accesso:    {_apiClient.CurrentRole ?? "non effettuato"}",
        _offlineQueueCount > 0 ? $"Operazioni in attesa di invio: {_offlineQueueCount}" : "Nessuna operazione in attesa di invio");

    private static string HostingName(string? hosting) => hosting switch
    {
        "cloud" => "cloud",
        "on-premise" => "server aziendale",
        null => "—",
        _ => "altro",
    };

    private void Remote_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (RustDesk.TryStart())
            {
                SetResult("RustDesk avviato: comunica all'assistenza ID e password che vedi nella sua finestra.", ok: true);
                return;
            }

            RustDesk.OpenDownloadPage();
            SetResult("RustDesk non è installato: si è aperta la pagina ufficiale. Scarica la versione per Windows, avviala e comunica ID e password all'assistenza.", ok: true);
        }
        catch (Exception exception)
        {
            ClientLog.Error("Teleassistenza: avvio RustDesk", exception);
            SetResult($"Impossibile avviare la sessione remota: {exception.Message}", ok: false);
        }
    }

    /// <summary>Saved on the Desktop and shown in Explorer, ready to attach to an email.</summary>
    private async void Package_Click(object sender, RoutedEventArgs e)
    {
        PackageButton.IsEnabled = false;
        try
        {
            JsonElement? diagnostics = null;
            if (_apiClient.CurrentRole == "Admin" && _reachable)
            {
                try
                {
                    diagnostics = await _apiClient.GetServerDiagnosticsAsync();
                }
                catch (Exception exception)
                {
                    ClientLog.Error("Teleassistenza: diagnostica server", exception);
                }
            }

            var snapshot = SupportPackage.Snapshot(_apiClient.BaseAddress.ToString(), _reachable, _info?.ServerVersion, _info?.Hosting,
                _apiClient.CurrentRole, _offlineQueueCount, diagnostics);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = SupportPackage.Create(snapshot, desktop, ClientLog.Directory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            SetResult($"Pacchetto creato sul Desktop: {Path.GetFileName(path)}. Allegalo all'email per l'assistenza.", ok: true);
        }
        catch (Exception exception)
        {
            ClientLog.Error("Teleassistenza: pacchetto diagnostico", exception);
            SetResult($"Impossibile creare il pacchetto: {exception.Message}", ok: false);
        }
        finally
        {
            PackageButton.IsEnabled = true;
        }
    }

    private void CopyInfo_Click(object sender, RoutedEventArgs e) => Copy(InfoLines());

    private void CopyServer_Click(object sender, RoutedEventArgs e) => Copy(RustDeskServerBox.Text);

    private void CopyKey_Click(object sender, RoutedEventArgs e) => Copy(RustDeskKeyBox.Text);

    private void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            SetResult("Copiato negli appunti.", ok: true);
        }
        catch
        {
            SetResult("Appunti non disponibili: seleziona il testo e copialo a mano.", ok: false);
        }
    }

    private void SetResult(string text, bool ok)
    {
        ResultText.Text = text;
        ResultText.Foreground = (Brush)FindResource(ok ? "SuccessTextBrush" : "DangerBrush");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
