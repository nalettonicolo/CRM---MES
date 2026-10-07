using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;

namespace CrmMes.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ApiClient _apiClient = new();
    private readonly UpdateService _updateService = new();
    private bool _isAuthenticated;
    private bool _canEditLayouts;
    private Guid _currentUserId;
    private string? _currentRole;
    private string? _refreshToken;
    private readonly DispatcherTimer _refreshTimer = new();
    private PurchaseOrderSummaryDto? _selectedOrder;
    private WithdrawalSlipSummaryDto? _selectedSlip;
    private ProductSummaryDto? _selectedProduct;
    private WorkOrderSummaryDto? _selectedWorkOrder;
    private MaterialLotSummaryDto? _selectedMaterialLot;
    private ShipmentSummaryDto? _selectedShipment;

    private bool _lowStockLoaded;
    private bool _missingLoaded;
    private bool _slipsLoaded;
    private bool _ordersLoaded;
    private bool _areasLoaded;
    private bool _usersLoaded;
    private bool _productsLoaded;
    private bool _workOrdersLoaded;
    private bool _materialLotsLoaded;
    private bool _dashboardLoaded;
    private bool _workCentersLoaded;
    private bool _carriersLoaded;
    private bool _shipmentsLoaded;
    private bool _suppliersLoaded;
    private bool _sitesLoaded;
    private bool _equipmentLoaded;
    private bool _maintenanceLoaded;
    private MaintenanceTaskDto? _selectedMaintenanceTask;
    private bool _siteFilterOptionsLoaded;
    private bool _syncingSiteFilters;
    private Guid? _siteFilterId;
    private bool _planningLoaded;
    private int _planningWeeks = 4;
    private bool _customersLoaded;
    private bool _quotesLoaded;
    private bool _marginsLoaded;
    private bool _transportDocumentsLoaded;
    private bool _subcontractingLoaded;
    private bool _haccpLoaded;
    private bool _siteReportsLoaded;
    private bool _invoicesLoaded;
    private bool _paymentScheduleLoaded;
    private bool _purchaseInvoicesLoaded;

    private static readonly Dictionary<int, string> PageTitles = new()
    {
        [0] = "Materiali",
        [1] = "Sotto scorta",
        [2] = "Materiali mancanti",
        [3] = "Distinte di prelievo",
        [4] = "Ordini fornitore",
        [5] = "Aree",
        [6] = "Utenti",
        [7] = "Prodotti",
        [8] = "Commesse",
        [9] = "Lotti materiali",
        [10] = "Cruscotto",
        [11] = "Centri di lavoro",
        [12] = "Corrieri",
        [13] = "Spedizioni",
        [14] = "Fornitori",
        [15] = "Ricerca catalogo",
        [16] = "Sedi",
        [17] = "Macchine",
        [18] = "Manutenzione",
        [19] = "Pianificazione",
        [20] = "Clienti",
        [21] = "Preventivi",
        [22] = "Controllo margini",
        [23] = "Documenti di trasporto",
        [24] = "Conto lavoro",
        [25] = "Registri HACCP",
        [26] = "Rapportini di cantiere",
        [27] = "Fatture",
        [28] = "Scadenziario",
        [29] = "Fatture passive",
    };

    private static readonly Dictionary<int, string> PageEyebrows = new()
    {
        [0] = "M A G A Z Z I N O",
        [1] = "M A G A Z Z I N O",
        [2] = "M A G A Z Z I N O",
        [3] = "M A G A Z Z I N O",
        [9] = "M A G A Z Z I N O",
        [4] = "A C Q U I S T I",
        [14] = "A C Q U I S T I",
        [15] = "A C Q U I S T I",
        [7] = "P R O D U Z I O N E",
        [8] = "P R O D U Z I O N E",
        [10] = "P R O D U Z I O N E",
        [11] = "P R O D U Z I O N E",
        [5] = "A M M I N I S T R A Z I O N E",
        [6] = "A M M I N I S T R A Z I O N E",
        [16] = "A M M I N I S T R A Z I O N E",
        [17] = "M A N U T E N Z I O N E",
        [18] = "M A N U T E N Z I O N E",
        [12] = "S P E D I Z I O N I",
        [13] = "S P E D I Z I O N I",
        [19] = "P R O D U Z I O N E",
        [20] = "V E N D I T E",
        [21] = "V E N D I T E",
        [22] = "D I R E Z I O N E",
        [23] = "S P E D I Z I O N I",
        [24] = "A C Q U I S T I",
        [25] = "P R O D U Z I O N E",
        [26] = "P R O D U Z I O N E",
        [27] = "V E N D I T E",
        [28] = "A M M I N I S T R A Z I O N E",
        [29] = "A C Q U I S T I",
    };

    private static readonly Dictionary<int, string> PageHelpTexts = new()
    {
        [27] = "Fatture elettroniche (FatturaPA). \"Fattura da DDT\" crea la fattura differita dai DDT emessi di un cliente, già con i prezzi venduti (prezzo della commessa o riga del preventivo): controlla IVA e prezzi, poi \"Emetti\". Il file XML si carica gratis sul portale Fatture e Corrispettivi dell'Agenzia delle Entrate o si consegna al commercialista. Servono i dati fiscali dell'azienda (Amministrazione) e del cliente (Clienti > Dati fiscali).",
        [28] = "Scadenze da pagare (fatture passive importate) e da incassare (fatture emesse con data di pagamento). Segna pagato o registra un sollecito: non invia email automatiche.",
        [29] = "Fatture elettroniche ricevute dai fornitori: importa il file XML FatturaPA. Il fornitore viene collegato o creato in anagrafica; le scadenze di pagamento compaiono nello Scadenziario.",
        [25] = "Piano HACCP: i punti di controllo critici con i loro limiti e le letture registrate. Una lettura fuori limite richiede l'azione correttiva; le letture non si modificano né si cancellano, perché il registro vale come prova per i controlli. \"Registro\" esporta il periodo per l'ispezione.",
        [26] = "Rapportini di intervento presso il cliente: lavori eseguiti, ore per tecnico, materiali installati e firma del cliente. La firma si raccoglie sul posto dalla pagina web dei tecnici (telefono o tablet). Firmato, il rapportino si blocca, le ore entrano nella commessa e i materiali nel suo costo reale.",
        [23] = "Documenti di trasporto (DDT) per qualsiasi causale: vendita, conto lavorazione, riparazione, reso, conto visione. Una bozza si modifica liberamente e non ha numero; \"Emetti\" assegna il numero progressivo dell'anno e la blocca. Un DDT emesso non si modifica né si cancella: si annulla (resta in archivio con il suo numero). Da una commessa, \"Crea DDT\" prepara la bozza con cliente, prodotto, lotto e quantità.",
        [24] = "Materiale presso i terzisti: ogni riga dei DDT con causale \"Conto lavorazione\" ancora da rientrare, con quanto è già tornato o è stato scartato. In rosso i rientri oltre la data prevista. Doppio click per aprire il DDT e registrare un rientro, anche parziale, con il riferimento al DDT del terzista.",
        [0] = "Elenco dei materiali a magazzino: codice, descrizione, unità di misura, giacenza. Da qui si crea un nuovo materiale, si cerca per codice/descrizione e si importa un catalogo Excel di un fornitore.",
        [1] = "Materiali la cui giacenza è scesa sotto la scorta minima impostata. \"Scansiona\" crea automaticamente una richiesta di materiale mancante per ciascuno di quelli non ancora richiesti.",
        [2] = "Materiali richiesti ma non ancora disponibili: generati automaticamente chiudendo una distinta di prelievo che porta un materiale sotto scorta, oppure dallo scan sottoscorte. Restano aperti finché non arrivano da un ordine fornitore.",
        [3] = "Documenti di prelievo materiale da magazzino verso un'area (es. reparto produzione). Bozza -> Pronta -> Chiusa (scarica davvero la giacenza) oppure Annullata. Solo le distinte in bozza si possono modificare.",
        [4] = "Ordini di acquisto verso i fornitori. Bozza -> Confermato -> ricevuto (anche parzialmente). Ricevere un ordine carica la giacenza e crea un lotto materiale tracciabile per ogni riga.",
        [5] = "Aree/reparti dell'azienda a cui è possibile destinare una distinta di prelievo o assegnare una commessa.",
        [6] = "Utenti abilitati ad accedere al gestionale, con il rispettivo ruolo (Admin, Warehouse, Purchasing, Sales per clienti e preventivi, Operator). Solo un Admin può crearne di nuovi. Doppio click su una riga per il dettaglio: aree assegnate e attività recente (fasi, fermi, non conformità, collegate per identità PIN, non più solo per nome libero).",
        [7] = "Anagrafica dei prodotti che si costruiscono: distinta base (materiali necessari) e ciclo di lavoro (fasi di produzione). Da qui si genera automaticamente la struttura di ogni nuova commessa.",
        [8] = "Commesse di produzione: quantità da costruire di un prodotto, con le fasi del ciclo di lavoro tracciate una per una (avvio/completamento, minuti effettivi, performance). Rilasciare una commessa verifica la disponibilità dei materiali.",
        [9] = "Tracciabilità dei lotti materiale: ogni ingresso di giacenza (ricezione ordine, carico manuale) genera un lotto. Il consumo nelle distinte di prelievo avviene FIFO dal lotto più vecchio; aprendo un lotto si vede dove è stato usato.",
        [10] = "Indicatori aggregati sulle commesse degli ultimi giorni: quante per stato, fasi completate, performance media (minuti stimati/effettivi), percentuale di consegne puntuali. Copre solo la componente \"Performance\", non un OEE completo.",
        [11] = "Anagrafica dei centri di lavoro (reparti/linee) con la loro capacità produttiva giornaliera in minuti, e il confronto con il carico di lavoro attualmente in attesa su ciascuno. È una stima di arretrato, non una pianificazione a calendario con date precise.",
        [12] = "Anagrafica dei corrieri usati per le spedizioni in ingresso e in uscita.",
        [13] = "Spedizioni in ingresso (es. da un fornitore) e in uscita (es. verso un cliente), collegabili opzionalmente a un ordine fornitore o a una commessa. Ciclo: In preparazione -> Spedita -> Consegnata, oppure Annullata.",
        [14] = "Anagrafica fornitori. Doppio click su una riga per il dettaglio: ordini d'acquisto e voci di catalogo collegate.",
        [15] = "Ricerca rapida nel catalogo (materiali collegati a un fornitore con part number/prezzo/lead time), senza dover importare un PDF: utile quando il catalogo del fornitore è il suo sito web.",
        [16] = "Sedi fisiche della stessa azienda (es. un secondo stabilimento). Non sono aziende separate: utenti, materiali, fornitori e prodotti restano condivisi — la sede è solo una dimensione per filtrare/riportare aree e centri di lavoro. Doppio click su una sede per vedere quali aree e centri di lavoro le appartengono.",
        [17] = "Anagrafica macchine/asset per la manutenzione — il controparte di Centri di lavoro, che resta l'unità di capacità. Doppio click su una macchina per lo storico degli interventi di manutenzione registrati su di essa.",
        [18] = "Interventi di manutenzione preventiva (pianificata, con scadenza ed eventuale ricorrenza — completandolo genera subito la prossima occorrenza) o correttiva (a fronte di un guasto). È il pezzo che permette di intervenire sul fattore Disponibilità dell'OEE, non solo misurarlo.",
        [19] = "Board settimanale che aggrega tutte le scadenze già tracciate altrove — consegne commesse, consegne previste ordini fornitore, spedizioni, interventi di manutenzione — colorate per tipo, filtrabili per tipo/stato/sede, con un cruscotto di riepilogo (in ritardo/questa settimana/prossima settimana/totale). Non introduce nuovi dati: aggrega e colora quello che esiste già nelle rispettive sezioni.",
        [20] = "Anagrafica clienti: a chi si fanno i preventivi e per chi si costruiscono le commesse. Doppio click su un cliente per il dettaglio: preventivi e commesse collegati. Crea e modifica: Admin e ruolo Sales.",
        [22] = "Riservato ad Admin e Management. Per le commesse più recenti: prezzo di vendita, costo stimato (distinta e ciclo), costo reale (materiali prelevati al prezzo d'acquisto del lotto o a listino, minuti delle fasi e ore registrate per la tariffa oraria del centro di lavoro) e margine. I totali contano solo le commesse con un prezzo di vendita. Gli avvisi segnalano dati mancanti (prezzi, tariffe) che rendono il costo incompleto.",
        [21] = "Preventivi ai clienti. Bozza -> Inviato -> Accettato o Rifiutato; solo una bozza si può modificare (doppio click o \"Modifica\"). \"Crea commesse\" trasforma un preventivo accettato in una commessa in bozza per ogni riga collegata a un prodotto, una volta sola. Nell'editor, \"Stima prezzo da distinta\" calcola il costo materiali dai listini fornitori più il ricarico indicato (manodopera esclusa).",
    };

#if DEBUG
    // Login automatico solo nelle build di sviluppo (#if DEBUG: assente dalle build Release e dalle
    // release pubblicate). Le credenziali NON stanno nel codice: il repository è pubblico, e fino al
    // 2026-09-29 email e password di un account Admin reale erano scritte qui, leggibili da chiunque.
    // Si leggono dalle variabili d'ambiente utente CRMMES_DEV_EMAIL e CRMMES_DEV_PASSWORD del PC di
    // sviluppo; se mancano, si fa il login a mano come in produzione.
    private static readonly string? DevLoginEmail = Environment.GetEnvironmentVariable("CRMMES_DEV_EMAIL");
    private static readonly string? DevLoginPassword = Environment.GetEnvironmentVariable("CRMMES_DEV_PASSWORD");
#endif

    public MainWindow()
    {
        InitializeComponent();
        MaximizeToWorkArea.Attach(this);
        NavMaterials.IsChecked = true;
        CopySupport.Attach(WorkOrdersList, ("Copia codice commessa", row => ((WorkOrderSummaryDto)row).Code), ("Copia lotto", row => ((WorkOrderSummaryDto)row).ProductLotNumber));
        CopySupport.Attach(MaterialLotsList, ("Copia lotto", row => ((MaterialLotSummaryDto)row).LotNumber), ("Copia codice materiale", row => ((MaterialLotSummaryDto)row).MaterialCode));
        CopySupport.Attach(MaterialsList, ("Copia codice materiale", row => ((MaterialDto)row).Code));
        CopySupport.Attach(TransportDocumentsList, ("Copia numero DDT", row => ((TransportDocumentSummaryDto)row).DocumentCode));
        CopySupport.Attach(SiteReportsList, ("Copia codice rapportino", row => ((SiteReportSummaryDto)row).Code), ("Copia codice commessa", row => ((SiteReportSummaryDto)row).WorkOrderCode));
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        _refreshTimer.Tick += RefreshTimer_Tick;
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _refreshTimer.Stop();
        if (_refreshToken is not null)
        {
            await _apiClient.LogoutAsync(_refreshToken);
        }
    }

    private async void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        if (_refreshToken is null)
        {
            return;
        }

        try
        {
            var auth = await _apiClient.RefreshAsync(_refreshToken);
            if (auth.TwoFactorSetupRequired)
            {
                // The Admin made two-factor compulsory for this role while the session was open.
                auth = await RequireTwoFactorSetupAsync(auth) ?? throw new InvalidOperationException("Verifica in due passaggi non attivata.");
            }

            CompleteLogin(auth, reloadMaterials: false);
        }
        catch (InvalidOperationException)
        {
            // Il refresh token non è più valido: l'utente dovrà rifare il login manualmente.
            _isAuthenticated = false;
            LoginPanel.Visibility = Visibility.Visible;
            DashboardPanel.Visibility = Visibility.Collapsed;
            LoginError.Text = "Sessione scaduta, effettua di nuovo il login.";
        }
    }

    private void ScheduleTokenRefresh(DateTime expiresAt)
    {
        _refreshTimer.Stop();
        var delay = expiresAt - DateTime.UtcNow - TimeSpan.FromMinutes(2);
        _refreshTimer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(5);
        _refreshTimer.Start();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object sender, EventArgs e)
    {
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        // Drop the resize grip when maximized so WindowChrome does not push content past the work area.
        if (WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.ResizeBorderThickness = WindowState == WindowState.Maximized ? new Thickness(0) : new Thickness(6);
        }

        RootGrid.Margin = new Thickness(0);
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tagText } radioButton || !int.TryParse(tagText, out var index))
        {
            return;
        }

        MainTabs.SelectedIndex = index;
        PageTitle.Text = PageTitles.GetValueOrDefault(index, string.Empty);
        PageEyebrow.Text = PageEyebrows.GetValueOrDefault(index, string.Empty);
        PageHelpIcon.Text = PageHelpTexts.GetValueOrDefault(index, string.Empty);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _apiClient.SetBaseUrl(ClientSettings.Load().ApiBaseUrl);
#if !DEBUG
        // Installed with "collegherò il server in seguito": ask for the address first. No admin check is
        // possible (there is no server yet), exactly as when the configured server is unreachable.
        if (!ClientSettings.IsConfigured())
        {
            ConnectionStatus.Text = "Programma non ancora collegato a un server";
            var window = new SettingsWindow(_apiClient) { Owner = this };
            window.ShowDialog();
            if (!window.SettingsChanged)
            {
                LoginButton.IsEnabled = false;
                LoginError.Text = "Collega il programma al server da \"Impostazioni server\" qui sotto.";
                return;
            }
        }
#endif
        await ConnectAsync();
    }

    private async void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        // The sidebar entry only appears once logged in as Admin (see CompleteLogin), so no extra check
        // is needed there. The login-screen entry has no logged-in user yet to check a role against, so
        // it asks for Admin credentials first instead — but only when the currently configured server is
        // actually reachable: if it isn't, that's exactly the "fix a wrong/unreachable address" scenario
        // this link exists for, and there is no server to verify credentials against anyway.
        if (sender == LoginSettingsButton && LoginButton.IsEnabled)
        {
            var gate = new AdminGateWindow(_apiClient) { Owner = this };
            gate.ShowDialog();
            if (!gate.Verified)
            {
                return;
            }
        }

        var window = new SettingsWindow(_apiClient) { Owner = this };
        window.ShowDialog();
        if (window.SettingsChanged)
        {
            await ConnectAsync();
        }
    }

    private void ShopFloorTerminalButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new ShopFloorTerminalWindow(_apiClient) { Owner = this };
        window.ShowDialog();
    }

    private void PlanningBoardButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new PlanningBoardWindow(_apiClient, _currentRole == "Admin") { Owner = this };
        window.ShowDialog();
    }

    private async Task ConnectAsync()
    {
        try
        {
            ConnectionStatus.Text = "Avvio collegamento...";
            var healthy = await _apiClient.EnsureLocalApiAsync();
            // A free-tier server that was asleep answers "unavailable" for up to a minute while it wakes
            // up: keep trying instead of declaring it down at the first attempt.
            for (var attempt = 1; !healthy && !_apiClient.BaseAddress.IsLoopback && attempt <= 8; attempt++)
            {
                ConnectionStatus.Text = $"Server in avvio, attendere... ({attempt * 10} s)";
                await Task.Delay(TimeSpan.FromSeconds(10));
                healthy = await _apiClient.EnsureLocalApiAsync();
            }

            ConnectionStatus.Text = healthy ? "Server online" : "Server non raggiungibile";
            LoginButton.IsEnabled = healthy;

#if DEBUG
            if (healthy && !string.IsNullOrWhiteSpace(DevLoginEmail) && !string.IsNullOrWhiteSpace(DevLoginPassword))
            {
                try
                {
                    var auth = await _apiClient.LoginAsync(DevLoginEmail, DevLoginPassword);
                    CompleteLogin(auth);
                }
                catch (InvalidOperationException exception)
                {
                    LoginError.Text = $"Auto-login disattivato: {exception.Message}";
                }
            }
#endif
        }
        catch
        {
            ConnectionStatus.Text = "API non disponibile";
            LoginButton.IsEnabled = false;
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        LoginError.Text = string.Empty;
        try
        {
            var auth = await _apiClient.LoginAsync(EmailBox.Text.Trim(), PasswordBox.Password);
            if (!string.IsNullOrEmpty(auth.TwoFactorChallenge))
            {
                var codeWindow = new TwoFactorCodeWindow(_apiClient, auth.TwoFactorChallenge) { Owner = this };
                if (codeWindow.ShowDialog() != true || codeWindow.Result is null)
                {
                    return;
                }

                auth = codeWindow.Result;
            }

            if (auth.TwoFactorSetupRequired)
            {
                auth = await RequireTwoFactorSetupAsync(auth);
                if (auth is null)
                {
                    return;
                }
            }

            PasswordBox.Password = string.Empty;
            CompleteLogin(auth);
        }
        catch (InvalidOperationException exception)
        {
            LoginError.Text = exception.Message;
        }
    }

    /// <summary>The role requires two-factor and the account hasn't set it up: the setup window, then a
    /// renewed session without the limit. Closing without activating ends the attempt.</summary>
    private async Task<AuthDto?> RequireTwoFactorSetupAsync(AuthDto auth)
    {
        _apiClient.SetToken(auth.Token);
        var window = new TwoFactorWindow(_apiClient) { Owner = this, Mandatory = true };
        window.ShowDialog();
        if (!window.Activated)
        {
            _apiClient.SetToken(string.Empty);
            LoginError.Text = "Per il tuo ruolo serve la verifica in due passaggi: accedi di nuovo per attivarla.";
            return null;
        }

        return await _apiClient.RefreshAsync(auth.RefreshToken);
    }

    private async void ImportArticlesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Scegli l'elenco articoli",
            Filter = "Excel o CSV (*.xlsx;*.csv)|*.xlsx;*.xlsm;*.csv|Tutti i file (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var window = new ArticleImportWindow(_apiClient, dialog.FileName) { Owner = this };
        window.ShowDialog();
        if (window.Imported)
        {
            await SearchMaterialsAsync();
        }
    }

    private async void ExportArticlesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (fileName, content) = await _apiClient.ExportArticlesAsync();
            var dialog = new Microsoft.Win32.SaveFileDialog { FileName = fileName, Filter = "Excel (*.xlsx)|*.xlsx" };
            if (dialog.ShowDialog(this) == true)
            {
                await System.IO.File.WriteAllBytesAsync(dialog.FileName, content);
            }
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, "Esporta articoli", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SupportButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SupportWindow(_apiClient, new OfflineActionQueue().Count) { Owner = this };
        window.ShowDialog();
    }

    private void ServiceButton_Click(object sender, RoutedEventArgs e) =>
        new ServiceWindow(_apiClient) { Owner = this }.ShowDialog();

    /// <summary>Il pulsante Layout appare a chi il server dice di poter modificare i layout: Admin e ruoli autorizzati.</summary>
    private async Task RefreshLayoutAccessAsync()
    {
        try
        {
            _canEditLayouts = (await _apiClient.GetLayoutAccessAsync()).CanEdit;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            _canEditLayouts = false;
        }

        ApplyEnabledModules();
    }

    private void LayoutButton_Click(object sender, RoutedEventArgs e) =>
        new LayoutEditorWindow(_apiClient) { Owner = this }.ShowDialog();

    private void EnergyButton_Click(object sender, RoutedEventArgs e) =>
        new EnergyWindow(_apiClient) { Owner = this }.ShowDialog();

    private void SecurityButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new TwoFactorWindow(_apiClient) { Owner = this };
        window.ShowDialog();
    }

    private async void ResetTwoFactorButton_Click(object sender, RoutedEventArgs e)
    {
        if (UsersList.SelectedItem is not UserRowDto user)
        {
            return;
        }

        if (MessageBox.Show(this, $"Azzerare la verifica in due passaggi di {user.Name}? Le sue sessioni verranno chiuse e al prossimo accesso la configurerà di nuovo.",
                "Verifica in due passaggi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.ResetUserTwoFactorAsync(user.Id);
            MessageBox.Show(this, $"Verifica in due passaggi di {user.Name} azzerata.", "Verifica in due passaggi", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidOperationException exception)
        {
            MessageBox.Show(this, exception.Message, "Verifica in due passaggi", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CompleteLogin(AuthDto auth, bool reloadMaterials = true)
    {
        _apiClient.SetToken(auth.Token);
        _isAuthenticated = true;
        _currentUserId = auth.UserId;
        _currentRole = auth.Role;
        _apiClient.CurrentRole = auth.Role;
        _ = FlagAvailableUpdateAsync();
        _ = ShowLicenseStateAsync();
        ApplyEnabledModules();
        _ = RefreshLayoutAccessAsync();
        CompanySetupButton.Visibility = auth.Role == "Admin" ? Visibility.Visible : Visibility.Collapsed;
        if (reloadMaterials && auth.Role is "Admin" or "Management")
        {
            // Management opens on what the floor is working on: released and in-progress jobs.
            WorkOrderStatusFilter.SelectedIndex = 1;
        }
        _refreshToken = auth.RefreshToken;
        ScheduleTokenRefresh(auth.ExpiresAt);
        LoginPanel.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Visible;
        ConnectionStatus.Text = $"Online: {auth.Name} ({auth.Role})";
        SidebarSettingsButton.Visibility = auth.Role == "Admin" ? Visibility.Visible : Visibility.Collapsed;
        if (reloadMaterials)
        {
            _ = SearchMaterialsAsync();
            _ = LoadCompanyProfileAsync(offerSetup: auth.Role == "Admin");
        }
    }

    /// <summary>Loads the company's industry and modules and adapts the sidebar. An Admin on a server
    /// nobody has configured yet gets the configuration window straight away (it can be postponed).</summary>
    private async Task LoadCompanyProfileAsync(bool offerSetup)
    {
        try
        {
            _apiClient.CompanyProfile = await _apiClient.GetCompanyProfileAsync();
        }
        catch (InvalidOperationException)
        {
            _apiClient.CompanyProfile = null; // every module stays visible, as before the configuration existed
        }

        try
        {
            _apiClient.DesktopAreas = await _apiClient.GetChannelAreasAsync("desktop");
        }
        catch (InvalidOperationException)
        {
            _apiClient.DesktopAreas = null;
        }

        try
        {
            ThemeApplier.Apply(await _apiClient.GetUiThemeAsync());
        }
        catch (InvalidOperationException)
        {
            // Server without theme endpoint: keep Officina brushes.
        }

        ApplyEnabledModules();
        if (offerSetup && _apiClient.CompanyProfile is { IsConfigured: false })
        {
            OpenCompanySetup();
        }
    }

    /// <summary>Shows only what the configured sector uses. Hiding never deletes data, and the API
    /// still enforces roles on its own.</summary>
    private void ApplyEnabledModules()
    {
        static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

        NavSalesSection.Visibility = Show(_apiClient.IsModuleEnabled("sales"));
        NavPurchasingSection.Visibility = Show(_apiClient.IsModuleEnabled("purchasing"));
        NavPlanning.Visibility = Show(_apiClient.IsModuleEnabled("planning"));
        PlanningBoardButton.Visibility = Show(_apiClient.IsModuleEnabled("planning"));
        ShopFloorTerminalButton.Visibility = Show(_apiClient.IsModuleEnabled("shopfloor"));
        NavMaintenanceSection.Visibility = Show(_apiClient.IsModuleEnabled("maintenance"));
        NavShippingSection.Visibility = Show(_apiClient.IsModuleEnabled("shipping"));
        NavSubcontracting.Visibility = Show(_apiClient.IsModuleEnabled("subcontracting"));
        NavInvoices.Visibility = Show(_apiClient.IsModuleEnabled("invoicing") && _apiClient.CurrentRole is "Admin" or "Sales" or "Management");
        var scheduleRole = _apiClient.CurrentRole is "Admin" or "Sales" or "Management" or "Purchasing";
        NavPaymentSchedule.Visibility = Show((_apiClient.IsModuleEnabled("invoicing") || _apiClient.IsModuleEnabled("purchasing")) && scheduleRole);
        NavPurchaseInvoices.Visibility = Show(_apiClient.IsModuleEnabled("purchasing") && _apiClient.CurrentRole is "Admin" or "Purchasing");
        CompanyFiscalButton.Visibility = Show(_apiClient.IsModuleEnabled("invoicing") && _apiClient.CurrentRole == "Admin");
        NavHaccp.Visibility = Show(_apiClient.IsModuleEnabled("haccp"));
        NavSiteReports.Visibility = Show(_apiClient.IsModuleEnabled("site-work"));
        ImportMetelMenuItem.Visibility = Show(_apiClient.IsModuleEnabled("metel"));
        MaterialFoodButton.Visibility = Show(_apiClient.IsModuleEnabled("food-labels"));
        ProductFoodButton.Visibility = Show(_apiClient.IsModuleEnabled("food-labels"));
        var lotExpiry = _apiClient.IsModuleEnabled("lot-expiry");
        SetLotExpiryButton.Visibility = Show(lotExpiry);
        LotExpiryColumn.Width = lotExpiry ? 110 : 0;
        NavManagementSection.Visibility = Show(_apiClient.CanViewMargins);
        WorkCenterRateButton.Visibility = Show(_apiClient.CanViewMargins);

        // Always-on areas the Admin may still keep off the desktop program (Canali di accesso).
        foreach (var item in new[] { NavMaterials, NavLowStock, NavMissing, NavSlips, NavMaterialLots })
        {
            item.Visibility = Show(_apiClient.IsAreaShown("warehouse"));
        }

        foreach (var item in new[] { NavProducts, NavWorkOrders })
        {
            item.Visibility = Show(_apiClient.IsAreaShown("production"));
        }

        foreach (var item in new[] { NavAreas, NavSites, NavWorkCenters })
        {
            item.Visibility = Show(_apiClient.IsAreaShown("registry"));
        }

        NavDashboard.Visibility = Show(_apiClient.IsAreaShown("dashboard"));
        NavUsers.Visibility = Show(_apiClient.IsAreaShown("users"));
        AccessChannelsButton.Visibility = Show(_apiClient.CurrentRole == "Admin");
        AppearanceButton.Visibility = Show(_apiClient.CurrentRole == "Admin");
        ServiceButton.Visibility = Show(_apiClient.IsModuleEnabled("service"));
        EnergyButton.Visibility = Show(_apiClient.IsModuleEnabled("energy-monitoring"));
        LayoutButton.Visibility = Show(_canEditLayouts);

        // A section just switched off may be the page on screen: fall back to the first one still shown.
        if (FindCheckedNavItem() is { Visibility: not Visibility.Visible } hidden)
        {
            hidden.IsChecked = false;
            var first = AllNavItems().FirstOrDefault(item => item.Visibility == Visibility.Visible);
            if (first is not null)
            {
                first.IsChecked = true;
            }
        }
    }

    private RadioButton[] AllNavItems() =>
        [NavMaterials, NavLowStock, NavMissing, NavSlips, NavMaterialLots, NavProducts, NavWorkOrders, NavWorkCenters,
         NavDashboard, NavCustomers, NavQuotes, NavInvoices, NavPaymentSchedule, NavOrders, NavSuppliers, NavCatalogSearch, NavPurchaseInvoices, NavSubcontracting,
         NavPlanning, NavHaccp, NavSiteReports, NavEquipment, NavMaintenance, NavCarriers, NavShipments,
         NavTransportDocuments, NavMargins, NavAreas, NavUsers, NavSites];

    private RadioButton? FindCheckedNavItem() => AllNavItems().FirstOrDefault(item => item.IsChecked == true);

    private void AccessChannelsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AccessChannelsWindow(_apiClient) { Owner = this };
        window.ShowDialog();
        if (window.Saved)
        {
            _ = LoadCompanyProfileAsync(offerSetup: false);
        }
    }

    private void AppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new AppearanceWindow(_apiClient) { Owner = this };
        window.ShowDialog();
    }

    // ---------- Documenti di trasporto

    private async Task LoadTransportDocumentsAsync()
    {
        try
        {
            if (TransportReasonFilter.ItemsSource is null)
            {
                var reasons = await _apiClient.GetTransportReasonsAsync();
                TransportReasonFilter.ItemsSource = new[] { new TransportReasonDto(string.Empty, "Tutte le causali") }.Concat(reasons).ToList();
                TransportReasonFilter.SelectedIndex = 0; // triggers a reload through the filter handler
                return;
            }

            BusyIndicator.Text = "Caricamento DDT...";
            TransportDocumentsList.ItemsSource = await _apiClient.GetTransportDocumentsAsync(
                (TransportStatusFilter.SelectedItem as ComboBoxItem)?.Tag as string,
                (TransportReasonFilter.SelectedItem as TransportReasonDto)?.Key,
                TransportSearchBox.Text);
            _transportDocumentsLoaded = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "DDT", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BusyIndicator.Text = string.Empty;
        }
    }

    private async void TransportFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isAuthenticated && IsLoaded)
        {
            await LoadTransportDocumentsAsync();
        }
    }

    private async void TransportSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LoadTransportDocumentsAsync();
        }
    }

    private async void RefreshTransportDocuments_Click(object sender, RoutedEventArgs e) => await LoadTransportDocumentsAsync();

    private async void NewTransportDocument_Click(object sender, RoutedEventArgs e) => await OpenTransportDocumentAsync(null);

    private async void TransportDocumentsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TransportDocumentsList.SelectedItem is TransportDocumentSummaryDto summary)
        {
            await OpenTransportDocumentAsync(summary.Id);
        }
    }

    private async Task OpenTransportDocumentAsync(Guid? id)
    {
        var window = new TransportDocumentWindow(_apiClient, id) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            await LoadTransportDocumentsAsync();
            _subcontractingLoaded = false; // conto lavoro reads the same documents
        }
    }

    private static readonly ExportColumn[] TransportExportColumns =
    [
        new("Numero DDT", row => ((TransportDocumentExportRowDto)row).DocumentNumber),
        new("Data", row => ((TransportDocumentExportRowDto)row).IssuedDate.ToString("dd/MM/yyyy")),
        new("Causale", row => ((TransportDocumentExportRowDto)row).Reason),
        new("Codice cliente", row => ((TransportDocumentExportRowDto)row).CustomerCode ?? string.Empty),
        new("Destinatario", row => ((TransportDocumentExportRowDto)row).RecipientName),
        new("Partita IVA", row => ((TransportDocumentExportRowDto)row).RecipientVatNumber ?? string.Empty),
        new("Commessa", row => ((TransportDocumentExportRowDto)row).WorkOrderCode ?? string.Empty),
        new("Riga", row => ((TransportDocumentExportRowDto)row).LineNumber.ToString()),
        new("Codice articolo", row => ((TransportDocumentExportRowDto)row).Code ?? string.Empty),
        new("Descrizione", row => ((TransportDocumentExportRowDto)row).Description),
        new("U.m.", row => ((TransportDocumentExportRowDto)row).Unit),
        new("Quantità", row => ((TransportDocumentExportRowDto)row).Quantity.ToString("0.###")),
        new("Lotto", row => ((TransportDocumentExportRowDto)row).LotNumber ?? string.Empty),
    ];

    /// <summary>Issued DDT lines of a period as a spreadsheet for the accounting system (deferred
    /// invoicing): the previous month by default, the usual monthly invoicing run.</summary>
    private async void ExportTransportDocuments_Click(object sender, RoutedEventArgs e)
    {
        var firstOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var prompt = new DatePromptWindow("Esporta DDT per la contabilità",
            "Righe dei DDT emessi nel periodo (esclusi bozze e annullati), una riga per articolo, da importare nel gestionale contabile per la fatturazione differita.",
            firstOfMonth.AddMonths(-1), to: firstOfMonth.AddDays(-1), isRange: true) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        IReadOnlyList<TransportDocumentExportRowDto> rows;
        try
        {
            rows = await _apiClient.GetTransportDocumentExportAsync(prompt.From!.Value, prompt.To!.Value);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Esportazione DDT", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (rows.Count == 0)
        {
            MessageBox.Show("Nessun DDT emesso nel periodo scelto.", "Esportazione DDT", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ExportList($"DDT {prompt.From:dd-MM-yyyy} {prompt.To:dd-MM-yyyy}", $"DDT_{prompt.From:yyyyMMdd}_{prompt.To:yyyyMMdd}.xlsx",
            TransportExportColumns, rows, asPdf: false);
    }

    // ---------- Invoices

    private Task LoadInvoicesAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var invoices = await _apiClient.GetInvoicesAsync((InvoiceStatusFilter.SelectedItem as ComboBoxItem)?.Tag as string);
        InvoicesList.ItemsSource = invoices;
        var pending = await _apiClient.GetUninvoicedDocumentsAsync();
        InvoicesInfoText.Text = pending.Count == 0
            ? "Nessun DDT in attesa di fattura."
            : $"{pending.Count} DDT emessi ancora da fatturare ({pending.Select(p => p.CustomerId).Distinct().Count()} clienti).";
        _invoicesLoaded = true;
    });

    private async void RefreshInvoices_Click(object sender, RoutedEventArgs e) => await LoadInvoicesAsync();

    private async void InvoiceStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isAuthenticated && IsLoaded && _invoicesLoaded)
        {
            await LoadInvoicesAsync();
        }
    }

    private async void InvoiceFromDocuments_Click(object sender, RoutedEventArgs e)
    {
        var picker = new InvoiceFromDocumentsWindow(_apiClient) { Owner = this };
        if (picker.ShowDialog() == true && picker.CreatedInvoice is { } invoice)
        {
            await OpenInvoiceAsync(invoice.Id);
        }
    }

    private async void NewInvoice_Click(object sender, RoutedEventArgs e) => await OpenInvoiceAsync(null);

    private async void InvoicesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (InvoicesList.SelectedItem is InvoiceSummaryDto invoice)
        {
            await OpenInvoiceAsync(invoice.Id);
        }
    }

    private async Task OpenInvoiceAsync(Guid? id)
    {
        new InvoiceWindow(_apiClient, id) { Owner = this }.ShowDialog();
        await LoadInvoicesAsync();
    }

    // ---------- Scadenziario e fatture passive

    private Task LoadPaymentScheduleAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var direction = (PaymentScheduleDirectionFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var status = (PaymentScheduleStatusFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        var entries = await _apiClient.GetPaymentScheduleAsync(
            string.IsNullOrEmpty(direction) ? null : direction,
            string.IsNullOrEmpty(status) ? null : status);
        PaymentScheduleList.ItemsSource = entries;
        var open = entries.Where(e => e.Status == "Open").ToList();
        PaymentScheduleInfoText.Text = open.Count == 0
            ? "Nessuna scadenza aperta con i filtri scelti."
            : $"{open.Count} scadenze aperte per {open.Sum(e => e.Amount):N2} €.";
        _paymentScheduleLoaded = true;
    });

    private async void RefreshPaymentSchedule_Click(object sender, RoutedEventArgs e) => await LoadPaymentScheduleAsync();

    private async void PaymentScheduleFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_isAuthenticated && IsLoaded && _paymentScheduleLoaded)
        {
            await LoadPaymentScheduleAsync();
        }
    }

    private async void MarkScheduleEntryPaid_Click(object sender, RoutedEventArgs e)
    {
        if (PaymentScheduleList.SelectedItem is not PaymentScheduleEntryDto entry)
        {
            MessageBox.Show("Seleziona una scadenza.", "Scadenziario", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (entry.Status == "Paid")
        {
            return;
        }

        await RunBusyAsync("Registrazione pagamento...", async () =>
        {
            await _apiClient.MarkScheduleEntryPaidAsync(entry.Id);
            await LoadPaymentScheduleAsync();
        });
    }

    private async void RemindScheduleEntry_Click(object sender, RoutedEventArgs e)
    {
        if (PaymentScheduleList.SelectedItem is not PaymentScheduleEntryDto entry)
        {
            MessageBox.Show("Seleziona una scadenza.", "Scadenziario", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await RunBusyAsync("Registrazione sollecito...", async () =>
        {
            await _apiClient.RemindScheduleEntryAsync(entry.Id);
            await LoadPaymentScheduleAsync();
        });
    }

    private Task LoadPurchaseInvoicesAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var invoices = await _apiClient.GetPurchaseInvoicesAsync();
        PurchaseInvoicesList.ItemsSource = invoices;
        PurchaseInvoicesInfoText.Text = invoices.Count == 0
            ? "Importa un file XML FatturaPA ricevuto dal fornitore."
            : $"{invoices.Count} fatture passive importate.";
        _purchaseInvoicesLoaded = true;
    });

    private async void RefreshPurchaseInvoices_Click(object sender, RoutedEventArgs e) => await LoadPurchaseInvoicesAsync();

    private async void ImportPurchaseInvoice_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "FatturaPA XML (*.xml)|*.xml|Tutti i file|*.*",
            Title = "Importa fattura passive",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync("Import fattura passive...", async () =>
        {
            var detail = await _apiClient.ImportPurchaseInvoiceXmlAsync(dialog.FileName);
            MessageBox.Show(
                $"Importata fattura {detail.DocumentNumber} di {detail.SupplierName} ({detail.Schedule.Count} scadenze).",
                "Fatture passive",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _paymentScheduleLoaded = false;
            await LoadPurchaseInvoicesAsync();
        });
    }

    private void CustomerFiscalButton_Click(object sender, RoutedEventArgs e)
    {
        if (CustomersList.SelectedItem is not CustomerDto customer)
        {
            MessageBox.Show("Seleziona un cliente.", "Dati fiscali", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var window = FiscalDataWindow.ForCustomer(_apiClient, customer);
        window.Owner = this;
        window.ShowDialog();
    }

    private void CompanyFiscalButton_Click(object sender, RoutedEventArgs e)
    {
        var window = FiscalDataWindow.ForCompany(_apiClient);
        window.Owner = this;
        window.ShowDialog();
    }

    // ---------- Food data, recall

    private void MaterialFoodButton_Click(object sender, RoutedEventArgs e)
    {
        if (MaterialsList.SelectedItem is not MaterialDto material)
        {
            MessageBox.Show("Seleziona un materiale.", "Dati alimentari", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        new MaterialFoodInfoWindow(_apiClient, material.Id) { Owner = this }.ShowDialog();
    }

    private void ProductFoodButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct is not null)
        {
            new ProductFoodInfoWindow(_apiClient, _selectedProduct.Id) { Owner = this }.ShowDialog();
        }
    }

    private void RecallLotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMaterialLot is not null)
        {
            var recall = RecallWindow.ForMaterialLot(_apiClient, _selectedMaterialLot.Id);
            recall.Owner = this;
            recall.ShowDialog();
        }
    }

    private void RecallProductLotButton_Click(object sender, RoutedEventArgs e)
    {
        var prompt = new TextPromptWindow("Richiamo lotto prodotto", "Lotto del prodotto finito da richiamare (lo trovi sull'etichetta o sulla commessa):") { Owner = this };
        if (prompt.ShowDialog() == true)
        {
            var recall = RecallWindow.ForProductLot(_apiClient, prompt.Value);
            recall.Owner = this;
            recall.ShowDialog();
        }
    }

    // ---------- HACCP

    private Task LoadHaccpAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var points = await _apiClient.GetHaccpControlPointsAsync(activeOnly: false);
        HaccpPointsList.ItemsSource = points.OrderBy(p => !p.IsActive).ThenBy(p => p.Location).ThenBy(p => p.Name).ToList();
        var open = points.Count(p => p.IsActive && p.LastNonCompliant);
        var neverRead = points.Count(p => p.IsActive && p.LastReadAt is null);
        HaccpInfoText.Text = points.Count == 0
            ? "Nessun punto di controllo: aggiungi quelli del piano HACCP con \"Nuovo punto\"."
            : $"{points.Count(p => p.IsActive)} punti attivi" + (open > 0 ? $", {open} con l'ultima lettura non conforme" : string.Empty)
              + (neverRead > 0 ? $", {neverRead} mai rilevati" : string.Empty) + ".";
        _haccpLoaded = true;
    });

    private async void RefreshHaccp_Click(object sender, RoutedEventArgs e) => await LoadHaccpAsync();

    private async void HaccpReading_Click(object sender, RoutedEventArgs e)
    {
        if (HaccpPointsList.SelectedItem is not HaccpControlPointDto point)
        {
            MessageBox.Show("Seleziona il punto di controllo.", "HACCP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!point.IsActive)
        {
            MessageBox.Show("Punto di controllo disattivato.", "HACCP", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (new HaccpReadingWindow(_apiClient, point) { Owner = this }.ShowDialog() == true)
        {
            await LoadHaccpAsync();
        }
    }

    private void HaccpPointsList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => HaccpReading_Click(sender, e);

    private async void NewHaccpPoint_Click(object sender, RoutedEventArgs e)
    {
        if (new HaccpControlPointWindow(_apiClient) { Owner = this }.ShowDialog() == true)
        {
            await LoadHaccpAsync();
        }
    }

    private async void EditHaccpPoint_Click(object sender, RoutedEventArgs e)
    {
        if (HaccpPointsList.SelectedItem is HaccpControlPointDto point &&
            new HaccpControlPointWindow(_apiClient, point) { Owner = this }.ShowDialog() == true)
        {
            await LoadHaccpAsync();
        }
    }

    private static readonly ExportColumn[] HaccpExportColumns =
    [
        new("Data e ora", row => ((HaccpReadingDto)row).ReadAtLocal.ToString("dd/MM/yyyy HH:mm")),
        new("Punto di controllo", row => ((HaccpReadingDto)row).ControlPointName),
        new("Luogo", row => ((HaccpReadingDto)row).Location ?? string.Empty),
        new("Valore", row => ((HaccpReadingDto)row).ValueText),
        new("Esito", row => ((HaccpReadingDto)row).OutcomeText),
        new("Azione correttiva", row => ((HaccpReadingDto)row).CorrectiveAction ?? string.Empty),
        new("Operatore", row => ((HaccpReadingDto)row).OperatorName ?? string.Empty),
        new("Note", row => ((HaccpReadingDto)row).Notes ?? string.Empty),
    ];

    private void HaccpRegisterExcel_Click(object sender, RoutedEventArgs e) => _ = ExportHaccpRegisterAsync(asPdf: false, nonCompliantOnly: false);

    private void HaccpRegisterPdf_Click(object sender, RoutedEventArgs e) => _ = ExportHaccpRegisterAsync(asPdf: true, nonCompliantOnly: false);

    private void HaccpNonCompliantPdf_Click(object sender, RoutedEventArgs e) => _ = ExportHaccpRegisterAsync(asPdf: true, nonCompliantOnly: true);

    /// <summary>The HACCP register of a period, for inspections (the current month by default).</summary>
    private async Task ExportHaccpRegisterAsync(bool asPdf, bool nonCompliantOnly)
    {
        var firstOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var prompt = new DatePromptWindow("Registro HACCP", "Periodo del registro da esportare.", firstOfMonth, to: DateTime.Today, isRange: true) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var readings = await _apiClient.GetHaccpReadingsAsync(prompt.From!.Value, prompt.To!.Value, nonCompliantOnly);
            if (readings.Count == 0)
            {
                MessageBox.Show("Nessuna lettura nel periodo.", "Registro HACCP", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var title = $"Registro HACCP{(nonCompliantOnly ? " - non conformità" : string.Empty)} {prompt.From:dd-MM-yyyy} {prompt.To:dd-MM-yyyy}";
            ExportList(title, $"HACCP_{prompt.From:yyyyMMdd}_{prompt.To:yyyyMMdd}.{(asPdf ? "pdf" : "xlsx")}",
                HaccpExportColumns, readings.OrderBy(r => r.ReadAt), asPdf);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Registro HACCP", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- Site reports

    private Task LoadSiteReportsAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var status = (SiteReportStatusFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        SiteReportsList.ItemsSource = await _apiClient.GetSiteReportsAsync(status: status);
        _siteReportsLoaded = true;
    });

    private async void RefreshSiteReports_Click(object sender, RoutedEventArgs e) => await LoadSiteReportsAsync();

    private async void SiteReportStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isAuthenticated && IsLoaded)
        {
            await LoadSiteReportsAsync();
        }
    }

    private async void NewSiteReport_Click(object sender, RoutedEventArgs e) => await OpenSiteReportAsync(null);

    private async void SiteReportsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SiteReportsList.SelectedItem is SiteReportSummaryDto report)
        {
            await OpenSiteReportAsync(report.Id);
        }
    }

    private async Task OpenSiteReportAsync(Guid? id)
    {
        var window = new SiteReportWindow(_apiClient, id) { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            await LoadSiteReportsAsync();
        }
    }

    private void OpenTechnicianPage_Click(object sender, RoutedEventArgs e)
    {
        var url = new Uri(_apiClient.BaseAddress, "tecnici/").ToString();
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Apri questo indirizzo sul telefono del tecnico:\n{url}\n\n{exception.Message}", "Pagina tecnici", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ---------- Conto lavoro

    private async Task LoadSubcontractingAsync()
    {
        try
        {
            BusyIndicator.Text = "Caricamento conto lavoro...";
            var rows = await _apiClient.GetOpenSubcontractingAsync();
            SubcontractingList.ItemsSource = rows;
            var overdue = rows.Count(row => row.IsOverdue);
            SubcontractingInfoText.Text = rows.Count == 0
                ? "Nessun materiale presso i terzisti."
                : $"{rows.Count} righe presso {rows.Select(row => row.SupplierName).Distinct().Count()} terzisti"
                  + (overdue > 0 ? $", {overdue} oltre la data di rientro prevista." : ".");
            _subcontractingLoaded = true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Conto lavoro", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            BusyIndicator.Text = string.Empty;
        }
    }

    private async void RefreshSubcontracting_Click(object sender, RoutedEventArgs e) => await LoadSubcontractingAsync();

    private async void SubcontractingList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SubcontractingList.SelectedItem is SubcontractingOpenLineDto row)
        {
            var window = new TransportDocumentWindow(_apiClient, row.DocumentId) { Owner = this };
            window.ShowDialog();
            if (window.Changed)
            {
                await LoadSubcontractingAsync();
                _transportDocumentsLoaded = false;
            }
        }
    }

    /// <summary>A new DDT already set to "Conto lavorazione": the subcontractor is picked in the window.</summary>
    private async void NewSubcontractingDocument_Click(object sender, RoutedEventArgs e)
    {
        var window = new TransportDocumentWindow(_apiClient, null, initialReason: "Subcontracting") { Owner = this };
        window.ShowDialog();
        if (window.Changed)
        {
            await LoadSubcontractingAsync();
            _transportDocumentsLoaded = false;
        }
    }

    private void CompanySetupButton_Click(object sender, RoutedEventArgs e) => OpenCompanySetup();

    private void OpenCompanySetup()
    {
        var window = new CompanySetupWindow(_apiClient) { Owner = this };
        window.ShowDialog();
        if (window.SavedProfile is { } profile)
        {
            _apiClient.CompanyProfile = profile;
            ApplyEnabledModules();
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        await SearchMaterialsAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await SearchMaterialsAsync();
        }
    }

    private async void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isAuthenticated || MainTabs.SelectedItem is not TabItem tab)
        {
            return;
        }

        switch (tab.Header as string)
        {
            case "Sotto scorta" when !_lowStockLoaded:
                await LoadLowStockAsync();
                break;
            case "Materiali mancanti" when !_missingLoaded:
                await LoadMissingMaterialsAsync();
                break;
            case "Distinte di prelievo" when !_slipsLoaded:
                await LoadWithdrawalSlipsAsync();
                break;
            case "Ordini fornitore" when !_ordersLoaded:
                await LoadPurchaseOrdersAsync();
                break;
            case "Aree" when !_areasLoaded:
                await LoadAreasAsync();
                break;
            case "Utenti" when !_usersLoaded:
                await LoadUsersAsync();
                break;
            case "Prodotti" when !_productsLoaded:
                await LoadProductsAsync();
                break;
            case "Commesse" when !_workOrdersLoaded:
                await EnsureSiteFilterOptionsAsync();
                await LoadWorkOrdersAsync();
                break;
            case "Lotti materiali" when !_materialLotsLoaded:
                await LoadMaterialLotsAsync();
                break;
            case "Cruscotto" when !_dashboardLoaded:
                await EnsureSiteFilterOptionsAsync();
                await LoadDashboardAsync();
                break;
            case "Centri di lavoro" when !_workCentersLoaded:
                await EnsureSiteFilterOptionsAsync();
                await LoadWorkCentersAsync();
                break;
            case "Corrieri" when !_carriersLoaded:
                await LoadCarriersAsync();
                break;
            case "Spedizioni" when !_shipmentsLoaded:
                await LoadShipmentsAsync();
                break;
            case "Fornitori" when !_suppliersLoaded:
                await LoadSuppliersAsync();
                break;
            case "Sedi" when !_sitesLoaded:
                await LoadSitesAsync();
                break;
            case "Macchine" when !_equipmentLoaded:
                await LoadEquipmentAsync();
                break;
            case "Manutenzione" when !_maintenanceLoaded:
                await LoadMaintenanceTasksAsync();
                break;
            case "Pianificazione" when !_planningLoaded:
                await EnsureSiteFilterOptionsAsync();
                await LoadPlanningAsync();
                break;
            case "Clienti" when !_customersLoaded:
                await LoadCustomersAsync();
                break;
            case "Preventivi" when !_quotesLoaded:
                await LoadQuotesAsync();
                break;
            case "Controllo margini" when !_marginsLoaded:
                await LoadMarginsAsync();
                break;
            case "Documenti di trasporto" when !_transportDocumentsLoaded:
                await LoadTransportDocumentsAsync();
                break;
            case "Conto lavoro" when !_subcontractingLoaded:
                await LoadSubcontractingAsync();
                break;
            case "Registri HACCP" when !_haccpLoaded:
                await LoadHaccpAsync();
                break;
            case "Rapportini di cantiere" when !_siteReportsLoaded:
                await LoadSiteReportsAsync();
                break;
            case "Fatture" when !_invoicesLoaded:
                await LoadInvoicesAsync();
                break;
            case "Scadenziario" when !_paymentScheduleLoaded:
                await LoadPaymentScheduleAsync();
                break;
            case "Fatture passive" when !_purchaseInvoicesLoaded:
                await LoadPurchaseInvoicesAsync();
                break;
        }
    }

    private void ExportMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: not null } button)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>Shared save-and-export flow: asks where to save, runs the export, reports the result.
    /// Reused by every screen's "Esporta" menu — see <see cref="ListExporter"/>.</summary>
    private void ExportList(string title, string defaultFileName, IReadOnlyList<ExportColumn> columns, IEnumerable<object>? rows, bool asPdf)
    {
        if (rows is null || !rows.Any())
        {
            MessageBox.Show("Non c'è nulla da esportare: la lista è vuota.", "Esporta", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = defaultFileName,
            Filter = asPdf ? "File PDF (*.pdf)|*.pdf" : "File Excel (*.xlsx)|*.xlsx"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            if (asPdf)
            {
                ListExporter.ExportToPdf(title, columns, rows, dialog.FileName);
            }
            else
            {
                ListExporter.ExportToExcel(title, columns, rows, dialog.FileName);
            }

            MessageBox.Show($"Esportazione completata:\n{dialog.FileName}", "Esporta", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Impossibile esportare: {exception.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static readonly ExportColumn[] MaterialExportColumns =
    [
        new("Codice", row => ((MaterialDto)row).Code),
        new("Descrizione", row => ((MaterialDto)row).Name),
        new("Unità", row => ((MaterialDto)row).Unit),
        new("Giacenza", row => ((MaterialDto)row).Stock.ToString("0.##")),
        new("Sotto scorta", row => ((MaterialDto)row).BelowMinimum ? "Sì" : "No"),
    ];

    private void ExportMaterialsExcel_Click(object sender, RoutedEventArgs e) =>
        ExportList("Materiali", "materiali.xlsx", MaterialExportColumns, MaterialsList.ItemsSource?.Cast<object>(), asPdf: false);

    private void ExportMaterialsPdf_Click(object sender, RoutedEventArgs e) =>
        ExportList("Materiali", "materiali.pdf", MaterialExportColumns, MaterialsList.ItemsSource?.Cast<object>(), asPdf: true);

    private async void ImportCatalogButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "File Excel (*.xlsx)|*.xlsx",
            Title = "Importa catalogo fornitore"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync("Importazione catalogo in corso...", async () =>
        {
            var summary = await _apiClient.ImportCatalogExcelAsync(dialog.FileName);
            MessageBox.Show(
                $"Righe importate: {summary.Imported}\nMateriali creati: {summary.CreatedMaterials}\nCollegamenti catalogo creati: {summary.CreatedLinks}",
                "Importazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
            await SearchMaterialsAsync();
        });
    }

    private async void ImportCatalogPdfButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "File PDF (*.pdf)|*.pdf",
            Title = "Importa catalogo fornitore (PDF)"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync("Importazione catalogo PDF in corso...", async () =>
        {
            var summary = await _apiClient.ImportCatalogPdfAsync(dialog.FileName);
            MessageBox.Show(
                $"Righe importate: {summary.Imported}\nMateriali creati: {summary.CreatedMaterials}\nCollegamenti catalogo creati: {summary.CreatedLinks}",
                "Importazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
            await SearchMaterialsAsync();
        });
    }

    private async void ImportMetelButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Listino Metel (*.txt;*.csv)|*.txt;*.csv|Tutti i file (*.*)|*.*",
            Title = "Importa listino Metel"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var supplier = new TextPromptWindow("Fornitore del listino",
            "Codice fornitore a cui collegare gli articoli. Lascia vuoto per usare la marca (sigla produttore) di ogni riga.")
        {
            Owner = this,
            AllowEmpty = true
        };
        if (supplier.ShowDialog() != true)
        {
            return;
        }

        await RunBusyAsync("Importazione listino Metel...", async () =>
        {
            var summary = await _apiClient.ImportMetelAsync(dialog.FileName, supplier.Value, null);
            MessageBox.Show(
                $"Righe importate: {summary.Imported}\nMateriali creati: {summary.CreatedMaterials}\nCollegamenti catalogo creati: {summary.CreatedLinks}",
                "Importazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
            await SearchMaterialsAsync();
        });
    }

    private async void NewMaterialButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateMaterialWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await SearchMaterialsAsync();
        }
    }

    private async void NewWithdrawalSlipButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<AreaDto> areas;
        try
        {
            areas = await _apiClient.GetAreasAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (areas.Count == 0)
        {
            MessageBox.Show("Crea prima almeno un'area.", "Nuova distinta", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new CreateWithdrawalSlipWindow(_apiClient, areas, _currentUserId) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadWithdrawalSlipsAsync();
        }
    }

    private async void NewPurchaseOrderButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<SupplierDto> suppliers;
        try
        {
            suppliers = await _apiClient.GetSuppliersAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (suppliers.Count == 0)
        {
            MessageBox.Show("Nessun fornitore attivo trovato. Importa un catalogo fornitore prima di creare un ordine.", "Nuovo ordine", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new CreatePurchaseOrderWindow(_apiClient, suppliers) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadPurchaseOrdersAsync();
        }
    }

    private async void NewSupplierButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateSupplierWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created && _suppliersLoaded)
        {
            await LoadSuppliersAsync();
        }
    }

    private async void NewSupplierTabButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateSupplierWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadSuppliersAsync();
        }
    }

    private async void RefreshSuppliersButton_Click(object sender, RoutedEventArgs e) => await LoadSuppliersAsync();

    private async void RefreshSitesButton_Click(object sender, RoutedEventArgs e) => await LoadSitesAsync();

    private async void RefreshEquipmentButton_Click(object sender, RoutedEventArgs e) => await LoadEquipmentAsync();

    private async void NewEquipmentButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateEquipmentWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadEquipmentAsync();
        }
    }

    private void EquipmentList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (EquipmentList.SelectedItem is EquipmentDto equipment)
        {
            var dialog = new EquipmentDetailWindow(_apiClient, equipment.Id) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private async void DeactivateEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: EquipmentDto equipment })
        {
            return;
        }

        if (MessageBox.Show($"Disattivare la macchina {equipment.Name}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Disattivazione in corso...", async () =>
        {
            await _apiClient.DeactivateEquipmentAsync(equipment.Id);
            await LoadEquipmentAsync();
        });
    }

    private async void RefreshMaintenanceButton_Click(object sender, RoutedEventArgs e) => await LoadMaintenanceTasksAsync();

    private async void MaintenanceStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_maintenanceLoaded)
        {
            await LoadMaintenanceTasksAsync();
        }
    }

    private void MaintenanceTasksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMaintenanceTask = MaintenanceTasksList.SelectedItem as MaintenanceTaskDto;
        CompleteMaintenanceTaskButton.IsEnabled = _selectedMaintenanceTask?.Status == "Pending";
    }

    private async void NewMaintenanceTaskButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var equipment = await _apiClient.GetEquipmentAsync();
            if (equipment.Count == 0)
            {
                MessageBox.Show("Crea prima almeno una macchina attiva.", "Nuovo intervento", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new CreateMaintenanceTaskWindow(_apiClient, equipment) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Created)
            {
                await LoadMaintenanceTasksAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void CompleteMaintenanceTask_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMaintenanceTask is null)
        {
            return;
        }

        var taskId = _selectedMaintenanceTask.Id;
        await RunBusyAsync("Registrazione in corso...", async () =>
        {
            await _apiClient.CompleteMaintenanceTaskAsync(taskId, _currentUserId, null);
            await LoadMaintenanceTasksAsync();
        });
    }

    private async void NewSiteButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateSiteWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadSitesAsync();
        }
    }

    private void SitesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SitesList.SelectedItem is SiteDto site)
        {
            var dialog = new SiteDetailWindow(_apiClient, site.Id) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private async void DeactivateSite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SiteDto site })
        {
            return;
        }

        if (MessageBox.Show($"Disattivare la sede {site.Name}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Disattivazione in corso...", async () =>
        {
            await _apiClient.DeactivateSiteAsync(site.Id);
            await LoadSitesAsync();
        });
    }

    private void SuppliersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SuppliersList.SelectedItem is SupplierDto supplier)
        {
            var dialog = new SupplierDetailWindow(_apiClient, supplier.Id) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private void UsersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (UsersList.SelectedItem is UserRowDto user)
        {
            var dialog = new UserDetailWindow(_apiClient, user.Id) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private void AreasList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AreasList.SelectedItem is AreaDto area)
        {
            var dialog = new AreaDetailWindow(_apiClient, area.Id) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private void CarriersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CarriersList.SelectedItem is CarrierDto carrier)
        {
            var dialog = new CarrierDetailWindow(_apiClient, carrier.Id) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private void WorkCentersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WorkCentersList.SelectedItem is WorkCenterDto workCenter)
        {
            var dialog = new WorkCenterDetailWindow(_apiClient, workCenter.Id) { Owner = this };
            dialog.ShowDialog();
        }
    }

    private async void SearchCatalog_Click(object sender, RoutedEventArgs e) => await RunCatalogSearchAsync();

    private async void CatalogSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await RunCatalogSearchAsync();
        }
    }

    private async Task RunCatalogSearchAsync()
    {
        var query = CatalogSearchBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        await RunBusyAsync("Ricerca in corso...", async () =>
        {
            CatalogSearchResultsList.ItemsSource = await _apiClient.SearchCatalogAsync(query);
        });
    }

    private void OpenCatalogResultWebsite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CatalogSearchResultDto result } || string.IsNullOrWhiteSpace(result.SupplierWebsite))
        {
            MessageBox.Show("Questo fornitore non ha un sito web registrato. Puoi aggiungerlo dalla scheda Fornitori.", "Nessun sito", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var url = result.SupplierWebsite.Contains("://") ? result.SupplierWebsite : $"https://{result.SupplierWebsite}";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Impossibile aprire il link: {exception.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void NewAreaButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateAreaWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadAreasAsync();
        }
    }

    private async void NewCustomerButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CustomerEditWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadCustomersAsync();
        }
    }

    private async void SearchCustomersButton_Click(object sender, RoutedEventArgs e) => await LoadCustomersAsync();

    private async void CustomerSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await LoadCustomersAsync();
        }
    }

    private async void CustomersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (CustomersList.SelectedItem is not CustomerDto customer)
        {
            return;
        }

        var dialog = new CustomerDetailWindow(_apiClient, customer.Id) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await LoadCustomersAsync();
        }
    }

    private async void DeactivateCustomer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CustomerDto customer })
        {
            return;
        }

        if (MessageBox.Show($"Disattivare il cliente {customer.Name}? Preventivi e commesse esistenti restano intatti.",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Disattivazione in corso...", async () =>
        {
            await _apiClient.DeactivateCustomerAsync(customer.Id);
            await LoadCustomersAsync();
        });
    }

    private Task LoadCustomersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        CustomersList.ItemsSource = await _apiClient.GetCustomersAsync(activeOnly: true, q: CustomerSearchBox.Text);
        _customersLoaded = true;
    });

    private Task LoadQuotesAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var selectedId = (QuotesList.SelectedItem as QuoteSummaryDto)?.Id;
        var quotes = await _apiClient.GetQuotesAsync();
        QuotesList.ItemsSource = quotes;
        QuotesList.SelectedItem = quotes.FirstOrDefault(q => q.Id == selectedId);
        _quotesLoaded = true;
        UpdateQuoteButtons();
    });

    private async void RefreshQuotesButton_Click(object sender, RoutedEventArgs e) => await LoadQuotesAsync();

    private void QuotesList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateQuoteButtons();

    /// <summary>Only the actions valid for the selected quote's state are enabled, so an impossible
    /// transition is never offered in the first place (the API refuses it anyway).</summary>
    private void UpdateQuoteButtons()
    {
        var quote = QuotesList.SelectedItem as QuoteSummaryDto;
        var status = quote?.Status;
        EditQuoteButton.IsEnabled = status == "Draft";
        SendQuoteButton.IsEnabled = status == "Draft";
        AcceptQuoteButton.IsEnabled = status is "Draft" or "Sent";
        RejectQuoteButton.IsEnabled = status is "Draft" or "Sent";
        ConvertQuoteButton.IsEnabled = status == "Accepted" && quote?.IsConverted == false;
        QuotePdfButton.IsEnabled = quote is not null;
    }

    private async void NewQuoteButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new QuoteEditorWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadQuotesAsync();
        }
    }

    private void QuotesList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => EditQuoteButton_Click(sender, e);

    private async void EditQuoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuotesList.SelectedItem is not QuoteSummaryDto { Status: "Draft" } summary)
        {
            return;
        }

        QuoteDto quote;
        try
        {
            quote = await _apiClient.GetQuoteAsync(summary.Id);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new QuoteEditorWindow(_apiClient, quote) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadQuotesAsync();
        }
    }

    private async void QuoteStatusButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string action } || QuotesList.SelectedItem is not QuoteSummaryDto quote)
        {
            return;
        }

        var question = action switch
        {
            "send" => $"Segnare il preventivo {quote.Code} come inviato al cliente? Dopo non sarà più modificabile.",
            "accept" => $"Il cliente {quote.CustomerName} ha accettato il preventivo {quote.Code}?",
            _ => $"Segnare il preventivo {quote.Code} come rifiutato?"
        };
        if (MessageBox.Show(question, "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Aggiornamento preventivo...", async () =>
        {
            await _apiClient.ChangeQuoteStatusAsync(quote.Id, action);
            await LoadQuotesAsync();
        });
    }

    private async void ConvertQuoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuotesList.SelectedItem is not QuoteSummaryDto quote)
        {
            return;
        }

        var dialog = new ConvertQuoteWindow(_apiClient, quote) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Result is not { } result)
        {
            return;
        }

        // The work orders tab caches its list per session: force a reload so the new jobs appear there.
        _workOrdersLoaded = false;
        await LoadQuotesAsync();
        MessageBox.Show(
            $"Create {result.WorkOrders.Count} commesse in bozza:\n" +
            string.Join("\n", result.WorkOrders.Select(w => $"{w.Code}  ({w.ProductCode} x {w.Quantity:0.##})")) +
            "\n\nLe trovi in Produzione > Commesse, pronte da rilasciare.",
            "Commesse create", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void QuotePdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (QuotesList.SelectedItem is not QuoteSummaryDto summary)
        {
            return;
        }

        QuoteDto quote;
        CustomerDto? customer;
        try
        {
            quote = await _apiClient.GetQuoteAsync(summary.Id);
            customer = (await _apiClient.GetCustomerDetailAsync(quote.CustomerId)).Customer;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"{quote.Code}.pdf",
            Filter = "File PDF (*.pdf)|*.pdf"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ListExporter.ExportQuote(quote, customer, dialog.FileName);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private Task LoadMarginsAsync() => RunBusyAsync("Calcolo dei margini...", async () =>
    {
        var status = (MarginsStatusCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        var overview = await _apiClient.GetMarginsAsync(status);
        var italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        MarginsRevenueText.Text = $"{overview.Revenue.ToString("N2", italian)} €";
        MarginsCostText.Text = $"{overview.ActualCost.ToString("N2", italian)} €";
        MarginsMarginText.Text = overview.MarginRatio is { } ratio
            ? $"{overview.Margin.ToString("N2", italian)} € ({ratio.ToString("P1", italian)})"
            : "-";
        MarginsLossText.Text = overview.LossMakingCount.ToString(italian);
        MarginsInfoText.Text =
            $"{overview.WorkOrderCount} commesse, {overview.PricedWorkOrderCount} con prezzo di vendita. " +
            "Doppio click per il dettaglio. Margine sotto il 15% in ambra, in perdita in rosso.";
        MarginsList.ItemsSource = overview.WorkOrders;
        _marginsLoaded = true;
    });

    private async void RefreshMargins_Click(object sender, RoutedEventArgs e) => await LoadMarginsAsync();

    private async void MarginsStatusCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_marginsLoaded)
        {
            await LoadMarginsAsync();
        }
    }

    private async void MarginsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MarginsList.SelectedItem is MarginRowDto row)
        {
            new WorkOrderCostWindow(_apiClient, row.WorkOrderId) { Owner = this }.ShowDialog();
            await LoadMarginsAsync();
        }
    }

    private async void WorkCenterRate_Click(object sender, RoutedEventArgs e)
    {
        if (WorkCentersList.SelectedItem is not WorkCenterDto workCenter)
        {
            MessageBox.Show("Seleziona prima un centro di lavoro nell'elenco.", "Tariffa oraria", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new HourlyRateWindow(_apiClient, workCenter) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Saved)
        {
            await LoadWorkCentersAsync();
        }
    }

    private async void NewCarrierButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateCarrierWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadCarriersAsync();
        }
    }

    private async void DeactivateCarrier_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: CarrierDto carrier })
        {
            return;
        }

        if (MessageBox.Show($"Disattivare il corriere {carrier.Name}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Disattivazione in corso...", async () =>
        {
            await _apiClient.DeactivateCarrierAsync(carrier.Id);
            await LoadCarriersAsync();
        });
    }

    private async void NewShipmentButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<CarrierDto> carriers;
        IReadOnlyList<PurchaseOrderSummaryDto> purchaseOrders;
        IReadOnlyList<WorkOrderSummaryDto> workOrders;
        try
        {
            carriers = await _apiClient.GetCarriersAsync();
            purchaseOrders = await _apiClient.GetPurchaseOrdersAsync();
            workOrders = await _apiClient.GetWorkOrdersAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (carriers.Count == 0)
        {
            MessageBox.Show("Crea prima almeno un corriere attivo.", "Nuova spedizione", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new CreateShipmentWindow(_apiClient, carriers, purchaseOrders, workOrders) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadShipmentsAsync();
        }
    }

    private async void ShipmentDirectionFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_shipmentsLoaded)
        {
            await LoadShipmentsAsync();
        }
    }

    private void ShipmentsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedShipment = ShipmentsList.SelectedItem as ShipmentSummaryDto;
        var hasSelection = _selectedShipment is not null;
        ShipShipmentButton.IsEnabled = hasSelection && _selectedShipment!.Status == "Preparing";
        DeliverShipmentButton.IsEnabled = hasSelection && _selectedShipment!.Status == "Shipped";
        CancelShipmentButton.IsEnabled = hasSelection && (_selectedShipment!.Status is "Preparing" or "Shipped");
    }

    private async void ShipShipmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedShipment is null)
        {
            return;
        }

        var shipmentId = _selectedShipment.Id;
        await RunBusyAsync("Spedizione in corso...", async () =>
        {
            await _apiClient.ShipShipmentAsync(shipmentId, null);
            await LoadShipmentsAsync();
        });
    }

    private async void DeliverShipmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedShipment is null)
        {
            return;
        }

        var shipmentId = _selectedShipment.Id;
        await RunBusyAsync("Registrazione consegna in corso...", async () =>
        {
            await _apiClient.DeliverShipmentAsync(shipmentId);
            await LoadShipmentsAsync();
        });
    }

    private async void CancelShipmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedShipment is null)
        {
            return;
        }

        if (MessageBox.Show($"Annullare la spedizione {_selectedShipment.Code}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var shipmentId = _selectedShipment.Id;
        await RunBusyAsync("Annullamento in corso...", async () =>
        {
            await _apiClient.CancelShipmentAsync(shipmentId);
            await LoadShipmentsAsync();
        });
    }

    private async void NewUserButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateUserWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadUsersAsync();
        }
    }

    private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SetPinButton.IsEnabled = UsersList.SelectedItem is UserRowDto;
        ResetPasswordButton.IsEnabled = UsersList.SelectedItem is UserRowDto;
        ResetTwoFactorButton.IsEnabled = UsersList.SelectedItem is UserRowDto;
    }

    private void ResetPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (UsersList.SelectedItem is UserRowDto user)
        {
            var dialog = new PasswordWindow(_apiClient, user) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Saved)
            {
                MessageBox.Show($"Password di {user.Name} reimpostata. Le sue sessioni aperte sono state chiuse.",
                    "Reimposta password", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }

    private void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new PasswordWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Saved)
        {
            MessageBox.Show("Password cambiata. Le sessioni aperte con questo account su altri PC termineranno a breve.",
                "Cambia password", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SetPinButton_Click(object sender, RoutedEventArgs e)
    {
        if (UsersList.SelectedItem is not UserRowDto user)
        {
            return;
        }

        var dialog = new SetUserPinWindow(_apiClient, user.Id, user.Name) { Owner = this };
        dialog.ShowDialog();
    }

    private async void NewProductButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateProductWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadProductsAsync();
        }
    }

    private void ProductsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedProduct = ProductsList.SelectedItem as ProductSummaryDto;
        var hasSelection = _selectedProduct is not null;
        OpenProductButton.IsEnabled = hasSelection;
        EditProductButton.IsEnabled = hasSelection;
        ProductFoodButton.IsEnabled = hasSelection;
    }

    private void ProductsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProductsList.SelectedItem is ProductSummaryDto product)
        {
            _ = OpenProductDetailAsync(product.Id);
        }
    }

    private async void OpenProductButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct is not null)
        {
            await OpenProductDetailAsync(_selectedProduct.Id);
        }
    }

    private async Task OpenProductDetailAsync(Guid productId)
    {
        var dialog = new ProductDetailWindow(_apiClient, productId) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Changed)
        {
            await LoadProductsAsync();
        }
    }

    private async void EditProductButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProduct is null)
        {
            return;
        }

        try
        {
            var existing = await _apiClient.GetProductAsync(_selectedProduct.Id);
            var dialog = new CreateProductWindow(_apiClient, existing) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Created)
            {
                await LoadProductsAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void NewWorkOrderButton_Click(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<ProductSummaryDto> products;
        IReadOnlyList<AreaDto> areas;
        try
        {
            products = await _apiClient.GetProductsAsync();
            areas = await _apiClient.GetAreasAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (products.Count == 0)
        {
            MessageBox.Show("Crea prima almeno un prodotto.", "Nuova commessa", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new CreateWorkOrderWindow(_apiClient, products, areas) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadWorkOrdersAsync();
        }
    }

    private void WorkOrdersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedWorkOrder = WorkOrdersList.SelectedItem as WorkOrderSummaryDto;
        var hasSelection = _selectedWorkOrder is not null;
        OpenWorkOrderButton.IsEnabled = hasSelection;
        EditWorkOrderButton.IsEnabled = hasSelection && _selectedWorkOrder!.Status == "Draft";
        ReleaseWorkOrderButton.IsEnabled = hasSelection && _selectedWorkOrder!.Status == "Draft";
        CancelWorkOrderButton.IsEnabled = hasSelection && _selectedWorkOrder!.Status is not ("Completed" or "Cancelled");
    }

    private void WorkOrdersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (WorkOrdersList.SelectedItem is WorkOrderSummaryDto order)
        {
            _ = OpenWorkOrderDetailAsync(order.Id);
        }
    }

    private async void OpenWorkOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkOrder is not null)
        {
            await OpenWorkOrderDetailAsync(_selectedWorkOrder.Id);
        }
    }

    private async Task OpenWorkOrderDetailAsync(Guid workOrderId)
    {
        try
        {
            var products = await _apiClient.GetProductsAsync(activeOnly: false);
            var productNames = products.ToDictionary(product => product.Id, product => product.Name);

            var dialog = new WorkOrderDetailWindow(_apiClient, workOrderId, productNames) { Owner = this };
            dialog.ShowDialog();
            await LoadWorkOrdersAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditWorkOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkOrder is null)
        {
            return;
        }

        try
        {
            var products = await _apiClient.GetProductsAsync();
            var areas = await _apiClient.GetAreasAsync();
            var existing = await _apiClient.GetWorkOrderAsync(_selectedWorkOrder.Id);
            var dialog = new CreateWorkOrderWindow(_apiClient, products, areas, existing) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Created)
            {
                await LoadWorkOrdersAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ReleaseWorkOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkOrder is null)
        {
            return;
        }

        var workOrderId = _selectedWorkOrder.Id;
        await RunBusyAsync("Rilascio in corso...", async () =>
        {
            await _apiClient.ReleaseWorkOrderAsync(workOrderId);
            await LoadWorkOrdersAsync();
        });
    }

    private async void CancelWorkOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedWorkOrder is null)
        {
            return;
        }

        if (MessageBox.Show($"Annullare la commessa {_selectedWorkOrder.Code}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var workOrderId = _selectedWorkOrder.Id;
        await RunBusyAsync("Annullamento in corso...", async () =>
        {
            await _apiClient.CancelWorkOrderAsync(workOrderId);
            await LoadWorkOrdersAsync();
        });
    }

    private async void NewMaterialLotButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateMaterialLotWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
        if (dialog.Created)
        {
            await LoadMaterialLotsAsync();
        }
    }

    private void MaterialLotsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMaterialLot = MaterialLotsList.SelectedItem as MaterialLotSummaryDto;
        OpenMaterialLotButton.IsEnabled = _selectedMaterialLot is not null;
        SetLotExpiryButton.IsEnabled = _selectedMaterialLot is not null;
        RecallLotButton.IsEnabled = _selectedMaterialLot is not null;
    }

    private void MaterialLotsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (MaterialLotsList.SelectedItem is MaterialLotSummaryDto lot)
        {
            new MaterialLotDetailWindow(_apiClient, lot.Id) { Owner = this }.ShowDialog();
        }
    }

    private void OpenMaterialLotButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMaterialLot is not null)
        {
            new MaterialLotDetailWindow(_apiClient, _selectedMaterialLot.Id) { Owner = this }.ShowDialog();
        }
    }

    /// <summary>Filters the loaded list as the user types: the last digits of a code are enough, the
    /// matches ending with the text come first.</summary>
    private void ApplyWorkOrderSearch()
    {
        var term = WorkOrderSearchBox.Text.Trim();
        WorkOrdersList.ItemsSource = term.Length == 0
            ? _workOrders
            : _workOrders
                .Where(o => o.Code.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            o.ProductLotNumber.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                            o.ProductName.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.Code.EndsWith(term, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ToList();
    }

    private void WorkOrderSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyWorkOrderSearch();

    private async void WorkOrderStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isAuthenticated && IsLoaded && _workOrdersLoaded)
        {
            await LoadWorkOrdersAsync();
        }
    }

    private async void RefreshMaterialLotsButton_Click(object sender, RoutedEventArgs e) => await LoadMaterialLotsAsync();

    private async void SetLotExpiryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedMaterialLot is not { } lot)
        {
            return;
        }

        var prompt = new DatePromptWindow("Scadenza lotto",
            $"Data di scadenza del lotto {lot.LotNumber} ({lot.MaterialCode}). Lascia vuoto per toglierla.",
            lot.ExpiryDate?.Date, allowEmpty: true) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await _apiClient.SetMaterialLotExpiryAsync(lot.Id, prompt.From);
            await LoadMaterialLotsAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Scadenza lotto", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Warns about lots in stock that are expired or expire within 30 days (food module only).</summary>
    private async Task ShowExpiringLotsInfoAsync()
    {
        if (!_apiClient.IsModuleEnabled("lot-expiry"))
        {
            return;
        }

        try
        {
            var expiring = await _apiClient.GetExpiringMaterialLotsAsync(30);
            var expired = expiring.Count(lot => lot.IsExpired);
            MaterialLotsInfoText.Text = expiring.Count == 0
                ? "Nessun lotto in giacenza scade nei prossimi 30 giorni."
                : $"{expired} lotti scaduti e {expiring.Count - expired} in scadenza entro 30 giorni ancora in giacenza. Il prelievo usa per primi i lotti che scadono prima.";
            MaterialLotsInfoText.Foreground = expired > 0 ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("TextSecondaryBrush");
        }
        catch
        {
            // Only an informational line: the list itself still loads.
        }
    }

    private async void RefreshDashboardButton_Click(object sender, RoutedEventArgs e) => await LoadDashboardAsync();

    private void WithdrawalSlipsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedSlip = WithdrawalSlipsList.SelectedItem as WithdrawalSlipSummaryDto;
        var hasSelection = _selectedSlip is not null;
        OpenSlipButton.IsEnabled = hasSelection;
        EditSlipButton.IsEnabled = hasSelection && _selectedSlip!.Status == "Draft";
        ReadySlipButton.IsEnabled = hasSelection && _selectedSlip!.Status == "Draft";
        CloseSlipButton.IsEnabled = hasSelection && (_selectedSlip!.Status is "Draft" or "Ready");
        CancelSlipButton.IsEnabled = hasSelection && _selectedSlip!.Status != "Closed" && _selectedSlip!.Status != "Cancelled";
    }

    private void WithdrawalSlipsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (WithdrawalSlipsList.SelectedItem is WithdrawalSlipSummaryDto slip)
        {
            _ = OpenSlipDetailAsync(slip.Id);
        }
    }

    private async void OpenSlipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSlip is not null)
        {
            await OpenSlipDetailAsync(_selectedSlip.Id);
        }
    }

    private async Task OpenSlipDetailAsync(Guid slipId)
    {
        try
        {
            var areas = await _apiClient.GetAreasAsync();
            var users = await _apiClient.GetUsersAsync();
            var areaNames = areas.ToDictionary(area => area.Id, area => area.Name);
            var userNames = users.ToDictionary(user => user.Id, user => user.Name);

            var dialog = new WithdrawalSlipDetailWindow(_apiClient, slipId, areaNames, userNames) { Owner = this };
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void EditSlipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSlip is null)
        {
            return;
        }

        try
        {
            var areas = await _apiClient.GetAreasAsync();
            var existing = await _apiClient.GetWithdrawalSlipAsync(_selectedSlip.Id);
            var dialog = new CreateWithdrawalSlipWindow(_apiClient, areas, _currentUserId, existing) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Created)
            {
                await LoadWithdrawalSlipsAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ReadySlipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSlip is null)
        {
            return;
        }

        var slipId = _selectedSlip.Id;
        await RunBusyAsync("Aggiornamento in corso...", async () =>
        {
            await _apiClient.MarkWithdrawalSlipReadyAsync(slipId);
            await LoadWithdrawalSlipsAsync();
        });
    }

    private async void CloseSlipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSlip is null)
        {
            return;
        }

        var slipId = _selectedSlip.Id;
        await RunBusyAsync("Chiusura in corso...", async () =>
        {
            await _apiClient.CloseWithdrawalSlipAsync(slipId);
            await LoadWithdrawalSlipsAsync();
        });
    }

    private async void CancelSlipButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedSlip is null)
        {
            return;
        }

        if (MessageBox.Show($"Annullare la distinta {_selectedSlip.Code}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var slipId = _selectedSlip.Id;
        await RunBusyAsync("Annullamento in corso...", async () =>
        {
            await _apiClient.CancelWithdrawalSlipAsync(slipId);
            await LoadWithdrawalSlipsAsync();
        });
    }

    private async void RefreshLowStockButton_Click(object sender, RoutedEventArgs e) => await LoadLowStockAsync();

    private async void ScanLowStockButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Scansione in corso...", async () =>
        {
            var result = await _apiClient.ScanLowStockAsync();
            MessageBox.Show(
                $"Materiali sotto minimo: {result.MaterialsBelowMinimum}\nNuove richieste create: {result.MissingMaterialsCreated}",
                "Scansione sottoscorte", MessageBoxButton.OK, MessageBoxImage.Information);
            await LoadLowStockAsync();
        });
    }

    private async void RefreshMissingButton_Click(object sender, RoutedEventArgs e) => await LoadMissingMaterialsAsync();

    private async void RefreshWithdrawalSlipsButton_Click(object sender, RoutedEventArgs e) => await LoadWithdrawalSlipsAsync();

    private async void RefreshPurchaseOrdersButton_Click(object sender, RoutedEventArgs e) => await LoadPurchaseOrdersAsync();

    private async void RefreshAreasButton_Click(object sender, RoutedEventArgs e) => await LoadAreasAsync();

    private async void RefreshUsersButton_Click(object sender, RoutedEventArgs e) => await LoadUsersAsync();

    private async void RefreshProductsButton_Click(object sender, RoutedEventArgs e) => await LoadProductsAsync();

    private async void RefreshWorkOrdersButton_Click(object sender, RoutedEventArgs e) => await LoadWorkOrdersAsync();

    private async void RefreshCarriersButton_Click(object sender, RoutedEventArgs e) => await LoadCarriersAsync();

    private async void RefreshShipmentsButton_Click(object sender, RoutedEventArgs e) => await LoadShipmentsAsync();

    private void PurchaseOrdersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedOrder = PurchaseOrdersList.SelectedItem as PurchaseOrderSummaryDto;
        var hasSelection = _selectedOrder is not null;
        EditOrderButton.IsEnabled = hasSelection && _selectedOrder!.Status == "Draft";
        ConfirmOrderButton.IsEnabled = hasSelection && _selectedOrder!.Status == "Draft";
        ReceiveOrderButton.IsEnabled = hasSelection && (_selectedOrder!.Status is "Confirmed" or "PartiallyReceived");
        CancelOrderButton.IsEnabled = hasSelection && (_selectedOrder!.Status is not ("Received" or "Cancelled"));
        ExpectedDeliveryPicker.IsEnabled = hasSelection;
        SetExpectedDeliveryButton.IsEnabled = hasSelection;
    }

    private async void SetExpectedDeliveryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrder is null || ExpectedDeliveryPicker.SelectedDate is not DateTime date)
        {
            return;
        }

        var orderId = _selectedOrder.Id;
        await RunBusyAsync("Aggiornamento consegna prevista...", async () =>
        {
            await _apiClient.SetPurchaseOrderExpectedDeliveryAsync(orderId, date);
        });
    }

    private async void EditOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrder is null)
        {
            return;
        }

        try
        {
            var suppliers = await _apiClient.GetSuppliersAsync();
            var existing = await _apiClient.GetPurchaseOrderAsync(_selectedOrder.Id);
            var dialog = new CreatePurchaseOrderWindow(_apiClient, suppliers, existing) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Created)
            {
                await LoadPurchaseOrdersAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ConfirmOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrder is null)
        {
            return;
        }

        var orderId = _selectedOrder.Id;
        await RunBusyAsync("Conferma in corso...", async () =>
        {
            await _apiClient.ConfirmPurchaseOrderAsync(orderId);
            await LoadPurchaseOrdersAsync();
        });
    }

    private async void ReceiveOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrder is null)
        {
            return;
        }

        var orderId = _selectedOrder.Id;
        await RunBusyAsync("Ricezione in corso...", async () =>
        {
            await _apiClient.ReceiveRemainingQuantityAsync(orderId);
            await LoadPurchaseOrdersAsync();
        });
    }

    private async void CancelOrderButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedOrder is null)
        {
            return;
        }

        if (MessageBox.Show($"Annullare l'ordine {_selectedOrder.Code}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        var orderId = _selectedOrder.Id;
        await RunBusyAsync("Annullamento in corso...", async () =>
        {
            await _apiClient.CancelPurchaseOrderAsync(orderId);
            await LoadPurchaseOrdersAsync();
        });
    }

    /// <summary>Checks for a newer release in the background after login and, if there is one, turns the
    /// sidebar's update link into a visible notice. Silent on any failure (offline, GitHub rate limit):
    /// the manual "Controlla aggiornamenti" still reports errors.</summary>
    /// <summary>A banner when the subscription is late (everything works) or suspended (only the overall
    /// view: details and changes are refused with the reason).</summary>
    private async Task ShowLicenseStateAsync()
    {
        try
        {
            var license = await _apiClient.GetLicenseAsync();
            if (license is not { Enabled: true } || license.Status == "active")
            {
                LicenseBanner.Visibility = Visibility.Collapsed;
                return;
            }

            LicenseBannerText.Text = license.Status == "suspended"
                ? $"Abbonamento sospeso. {license.Message ?? "È disponibile solo la consultazione generale: contatta l'assistenza per riattivarlo."}"
                : $"Attenzione: {license.Message ?? "pagamento dell'abbonamento in ritardo."} Tutto funziona normalmente.";
            LicenseBanner.Visibility = Visibility.Visible;
        }
        catch
        {
            LicenseBanner.Visibility = Visibility.Collapsed;
        }
    }

    private async Task FlagAvailableUpdateAsync()
    {
        if (_updateService.IsDevelopmentBuild)
        {
            return;
        }

        try
        {
            if (await _updateService.CheckAsync() is { } release)
            {
                UpdateButton.Content = $"Aggiornamento disponibile: {release.TagName}";
                UpdateButton.Foreground = (System.Windows.Media.Brush)FindResource("SidebarTextActiveBrush");
                UpdateButton.FontWeight = FontWeights.Bold;
            }
        }
        catch
        {
            // Background convenience only.
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            var release = await _updateService.CheckAsync();
            if (release is null)
            {
                MessageBox.Show($"Il programma è aggiornato ({_updateService.CurrentVersion.ToString(3)}).", "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var notes = release.Notes.Length > 800 ? release.Notes[..800] + "…" : release.Notes;
            var result = MessageBox.Show(
                $"È disponibile la versione {release.TagName} (installata: {_updateService.CurrentVersion.ToString(3)}).\n\n{notes}\n\n" +
                "Aggiornare ora? Il programma si chiude, si aggiorna e si riapre da solo.",
                "Aggiornamento disponibile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                BusyIndicator.Text = "Download dell'aggiornamento...";
                await _updateService.StartUpdateAsync(release);
                Application.Current.Shutdown();
            }
        }
        catch (System.Net.Http.HttpRequestException exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            MessageBox.Show("Nessuna release pubblicata su GitHub per questo repository.", "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Impossibile controllare o installare l'aggiornamento.\n{exception.Message}", "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    /// <summary>Runs an action with a busy cursor and status label, so long-running calls give immediate feedback instead of appearing frozen.</summary>
    private async Task RunBusyAsync(string label, Func<Task> action)
    {
        BusyIndicator.Text = label;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await action();
        }
        catch (System.Net.Http.HttpRequestException)
        {
            ConnectionStatus.Text = "Connessione persa";
            MessageBox.Show(
                $"Impossibile raggiungere il server all'indirizzo {_apiClient.BaseAddress}.\n" +
                "Verifica la connessione di rete o l'indirizzo configurato in Impostazioni.",
                "Connessione al server non disponibile",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Mouse.OverrideCursor = null;
            BusyIndicator.Text = string.Empty;
        }
    }

    private Task SearchMaterialsAsync() => RunBusyAsync(string.Empty, async () =>
    {
        MaterialsList.ItemsSource = await _apiClient.SearchMaterialsAsync(SearchBox.Text.Trim());
    });

    private Task LoadLowStockAsync() => RunBusyAsync(string.Empty, async () =>
    {
        LowStockList.ItemsSource = await _apiClient.GetLowStockAsync();
        _lowStockLoaded = true;
    });

    private Task LoadMissingMaterialsAsync() => RunBusyAsync(string.Empty, async () =>
    {
        MissingMaterialsList.ItemsSource = await _apiClient.GetMissingMaterialsAsync();
        _missingLoaded = true;
    });

    private Task LoadWithdrawalSlipsAsync() => RunBusyAsync(string.Empty, async () =>
    {
        WithdrawalSlipsList.ItemsSource = await _apiClient.GetWithdrawalSlipsAsync();
        _slipsLoaded = true;
        _selectedSlip = null;
        OpenSlipButton.IsEnabled = false;
        EditSlipButton.IsEnabled = false;
        ReadySlipButton.IsEnabled = false;
        CloseSlipButton.IsEnabled = false;
        CancelSlipButton.IsEnabled = false;
    });

    private Task LoadPurchaseOrdersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var orders = await _apiClient.GetPurchaseOrdersAsync();
        PurchaseOrdersList.ItemsSource = orders;
        _ordersLoaded = true;
        _selectedOrder = null;
        EditOrderButton.IsEnabled = false;
        ConfirmOrderButton.IsEnabled = false;
        ReceiveOrderButton.IsEnabled = false;
        CancelOrderButton.IsEnabled = false;
    });

    private Task LoadAreasAsync() => RunBusyAsync(string.Empty, async () =>
    {
        AreasList.ItemsSource = await _apiClient.GetAreasAsync();
        _areasLoaded = true;
    });

    private Task LoadCarriersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        CarriersList.ItemsSource = await _apiClient.GetCarriersAsync(activeOnly: false);
        _carriersLoaded = true;
    });

    private Task LoadSuppliersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        SuppliersList.ItemsSource = await _apiClient.GetSuppliersAsync();
        _suppliersLoaded = true;
    });

    private Task LoadSitesAsync() => RunBusyAsync(string.Empty, async () =>
    {
        SitesList.ItemsSource = await _apiClient.GetSitesAsync(activeOnly: false);
        _sitesLoaded = true;
    });

    private Task LoadEquipmentAsync() => RunBusyAsync(string.Empty, async () =>
    {
        EquipmentList.ItemsSource = await _apiClient.GetEquipmentAsync(activeOnly: false);
        _equipmentLoaded = true;
    });

    private Task LoadMaintenanceTasksAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var status = (MaintenanceStatusFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        MaintenanceTasksList.ItemsSource = await _apiClient.GetMaintenanceTasksAsync(string.IsNullOrEmpty(status) ? null : status);
        _maintenanceLoaded = true;
        _selectedMaintenanceTask = null;
        CompleteMaintenanceTaskButton.IsEnabled = false;
    });

    /// <summary>One row on the planning board, already shaped for display — week label used for the
    /// ListView's group headers, date pre-formatted, everything else copied straight from the API.</summary>
    public sealed record PlanningEntryRow(string Type, string Code, string Detail, string Kind, string Status, DateTime Date, string DateLabel, string WeekLabel);

    private static readonly HashSet<string> PlanningAllTypes = ["WorkOrder", "PurchaseOrder", "Shipment", "MaintenanceTask"];

    private Task LoadPlanningAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var selectedTypes = new List<string>();
        if (PlanningFilterWorkOrder.IsChecked == true) selectedTypes.Add("WorkOrder");
        if (PlanningFilterPurchaseOrder.IsChecked == true) selectedTypes.Add("PurchaseOrder");
        if (PlanningFilterShipment.IsChecked == true) selectedTypes.Add("Shipment");
        if (PlanningFilterMaintenanceTask.IsChecked == true) selectedTypes.Add("MaintenanceTask");

        // All four checked (the common case) means "no filter" server-side, same as none checked would
        // be meaningless to ask for — but an empty selection should show nothing, not everything, so it's
        // special-cased separately below instead of being sent as "no filter".
        var planning = selectedTypes.Count == 0
            ? new PlanningDto(DateTime.UtcNow, _planningWeeks, [], new PlanningDashboardDto(0, 0, 0, 0))
            : await _apiClient.GetPlanningAsync(
                weeks: _planningWeeks, siteId: _siteFilterId,
                types: selectedTypes.Count == PlanningAllTypes.Count ? null : selectedTypes);

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var rows = planning.Entries.Select(entry =>
        {
            var weekStart = StartOfWeek(entry.Date);
            var weekEnd = weekStart.AddDays(6);
            return new PlanningEntryRow(
                entry.Type, entry.Code, entry.Detail, entry.Kind,
                StatusToItalianTextConverter.Translate(entry.Status), entry.Date,
                entry.Date.ToString("d", culture),
                $"Settimana del {weekStart:dd/MM} - {weekEnd:dd/MM}");
        }).ToList();

        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlanningEntryRow.WeekLabel)));
        PlanningList.ItemsSource = view;

        PlanningOverdueText.Text = planning.Dashboard.Overdue.ToString();
        PlanningThisWeekText.Text = planning.Dashboard.ThisWeek.ToString();
        PlanningNextWeekText.Text = planning.Dashboard.NextWeek.ToString();
        PlanningTotalText.Text = planning.Dashboard.Total.ToString();

        _planningLoaded = true;
    });

    private static DateTime StartOfWeek(DateTime date)
    {
        var diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
        return date.Date.AddDays(-diff);
    }

    private async void RefreshPlanningButton_Click(object sender, RoutedEventArgs e) => await LoadPlanningAsync();

    private async void PlanningTypeFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_planningLoaded)
        {
            await LoadPlanningAsync();
        }
    }

    private async void PlanningWeeksCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlanningWeeksCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string tag || !int.TryParse(tag, out var weeks))
        {
            return;
        }

        _planningWeeks = weeks;
        if (_planningLoaded)
        {
            await LoadPlanningAsync();
        }
    }

    private Task LoadShipmentsAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var direction = (ShipmentDirectionFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        ShipmentsList.ItemsSource = await _apiClient.GetShipmentsAsync(string.IsNullOrEmpty(direction) ? null : direction);
        _shipmentsLoaded = true;
        _selectedShipment = null;
        ShipShipmentButton.IsEnabled = false;
        DeliverShipmentButton.IsEnabled = false;
        CancelShipmentButton.IsEnabled = false;
    });

    private Task LoadUsersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        UsersList.ItemsSource = await _apiClient.GetUsersAsync();
        _usersLoaded = true;
    });

    private Task LoadProductsAsync() => RunBusyAsync(string.Empty, async () =>
    {
        ProductsList.ItemsSource = await _apiClient.GetProductsAsync(activeOnly: false);
        _productsLoaded = true;
        _selectedProduct = null;
        OpenProductButton.IsEnabled = false;
        EditProductButton.IsEnabled = false;
    });

    /// <summary>Populates the "Sede" filter combos shared by Commesse/Cruscotto/Centri di lavoro, once —
    /// a synthetic "Tutte le sedi" entry (Id = Guid.Empty) sits first so "no filter" is a selectable
    /// option, not just an absence.</summary>
    private async Task EnsureSiteFilterOptionsAsync()
    {
        if (_siteFilterOptionsLoaded)
        {
            return;
        }

        var sites = await _apiClient.GetSitesAsync();
        var options = new List<SiteDto> { new(Guid.Empty, "Tutte le sedi", "", null, true) };
        options.AddRange(sites);

        _syncingSiteFilters = true;
        WorkOrdersSiteFilterCombo.ItemsSource = options;
        WorkOrdersSiteFilterCombo.SelectedIndex = 0;
        DashboardSiteFilterCombo.ItemsSource = options.ToList();
        DashboardSiteFilterCombo.SelectedIndex = 0;
        WorkCentersSiteFilterCombo.ItemsSource = options.ToList();
        WorkCentersSiteFilterCombo.SelectedIndex = 0;
        PlanningSiteFilterCombo.ItemsSource = options.ToList();
        PlanningSiteFilterCombo.SelectedIndex = 0;
        _syncingSiteFilters = false;

        _siteFilterOptionsLoaded = true;
    }

    /// <summary>Any of the three "Sede" combos changing updates the one shared filter and keeps the other
    /// two in sync, then reloads whichever tab is currently showing — so the filter behaves as one scope
    /// followed across Commesse/Cruscotto/Centri di lavoro, not three independent ones.</summary>
    private async void SiteFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSiteFilters || sender is not ComboBox combo)
        {
            return;
        }

        var selected = combo.SelectedItem as SiteDto;
        _siteFilterId = selected is null || selected.Id == Guid.Empty ? null : selected.Id;

        _syncingSiteFilters = true;
        foreach (var otherCombo in new[] { WorkOrdersSiteFilterCombo, DashboardSiteFilterCombo, WorkCentersSiteFilterCombo, PlanningSiteFilterCombo })
        {
            if (!ReferenceEquals(otherCombo, combo) && otherCombo.ItemsSource is IEnumerable<SiteDto> items)
            {
                otherCombo.SelectedItem = items.FirstOrDefault(s => s.Id == (selected?.Id ?? Guid.Empty));
            }
        }
        _syncingSiteFilters = false;

        switch ((MainTabs.SelectedItem as TabItem)?.Header as string)
        {
            case "Commesse":
                await LoadWorkOrdersAsync();
                break;
            case "Cruscotto":
                await LoadDashboardAsync();
                break;
            case "Centri di lavoro":
                await LoadWorkCentersAsync();
                break;
            case "Pianificazione":
                await LoadPlanningAsync();
                break;
        }
    }

    private IReadOnlyList<WorkOrderSummaryDto> _workOrders = [];

    private Task LoadWorkOrdersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        _workOrders = await _apiClient.GetWorkOrdersAsync(
            status: (WorkOrderStatusFilter.SelectedItem as ComboBoxItem)?.Tag as string, siteId: _siteFilterId);
        ApplyWorkOrderSearch();
        _workOrdersLoaded = true;
        _selectedWorkOrder = null;
        OpenWorkOrderButton.IsEnabled = false;
        EditWorkOrderButton.IsEnabled = false;
        ReleaseWorkOrderButton.IsEnabled = false;
        CancelWorkOrderButton.IsEnabled = false;
    });

    private Task LoadMaterialLotsAsync() => RunBusyAsync(string.Empty, async () =>
    {
        MaterialLotsList.ItemsSource = await _apiClient.GetMaterialLotsAsync();
        _materialLotsLoaded = true;
        _selectedMaterialLot = null;
        OpenMaterialLotButton.IsEnabled = false;
        SetLotExpiryButton.IsEnabled = false;
        RecallLotButton.IsEnabled = false;
        await ShowExpiringLotsInfoAsync();
    });

    private Task LoadDashboardAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var dashboard = await _apiClient.GetWorkOrderDashboardAsync(siteId: _siteFilterId);
        _dashboardLoaded = true;

        DashboardStatusList.ItemsSource = dashboard.WorkOrdersByStatus;
        OperationsCompletedText.Text = dashboard.OperationsCompletedInPeriod.ToString();
        AveragePerformanceText.Text = dashboard.AveragePerformanceRatio.HasValue
            ? dashboard.AveragePerformanceRatio.Value.ToString("P0")
            : "-";
        WorkOrdersCompletedText.Text = dashboard.WorkOrdersCompletedInPeriod.ToString();
        OnTimeRateText.Text = dashboard.OnTimeCompletionRate.HasValue
            ? dashboard.OnTimeCompletionRate.Value.ToString("P0")
            : "-";
        AvailabilityText.Text = dashboard.AvailabilityRatio.HasValue
            ? dashboard.AvailabilityRatio.Value.ToString("P0")
            : "-";
        QualityText.Text = dashboard.QualityRatio.HasValue
            ? dashboard.QualityRatio.Value.ToString("P0")
            : "-";
        OeeText.Text = dashboard.OeeRatio.HasValue
            ? dashboard.OeeRatio.Value.ToString("P0")
                + (dashboard.OeeSource == "Machine" ? " (macchine)"
                    : dashboard.OeeSource == "Hybrid" ? " (misto)"
                    : " (dichiarato)")
            : "-";
    });

    private Task LoadWorkCentersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        WorkCentersList.ItemsSource = await _apiClient.GetWorkCentersAsync(_siteFilterId);
        WorkCenterLoadList.ItemsSource = await _apiClient.GetWorkCenterLoadAsync(_siteFilterId);
        _workCentersLoaded = true;
    });

    private async void AddWorkCenter_Click(object sender, RoutedEventArgs e)
    {
        var code = WorkCenterCodeBox.Text.Trim();
        var name = WorkCenterNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) ||
            !NumberInput.TryParseDecimal(WorkCenterCapacityBox.Text, out var capacity) || capacity < 0)
        {
            WorkCenterErrorText.Text = "Inserisci codice, nome e una capacità giornaliera valida.";
            return;
        }

        try
        {
            await _apiClient.CreateWorkCenterAsync(code, name, capacity);
            WorkCenterErrorText.Text = string.Empty;
            WorkCenterCodeBox.Text = string.Empty;
            WorkCenterNameBox.Text = string.Empty;
            WorkCenterCapacityBox.Text = "480";
            await LoadWorkCentersAsync();
        }
        catch (Exception exception)
        {
            WorkCenterErrorText.Text = exception.Message;
        }
    }

    private async void DeactivateWorkCenter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: WorkCenterDto workCenter })
        {
            return;
        }

        if (MessageBox.Show($"Disattivare il centro di lavoro {workCenter.Name}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeactivateWorkCenterAsync(workCenter.Id);
            await LoadWorkCentersAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
