using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private Guid _currentUserId;
    private string? _currentRole;
    private string? _refreshToken;
    private readonly DispatcherTimer _refreshTimer = new();
    private PurchaseOrderSummaryDto? _selectedOrder;
    private WithdrawalSlipSummaryDto? _selectedSlip;
    private ProductSummaryDto? _selectedProduct;
    private WorkOrderSummaryDto? _selectedWorkOrder;
    private MaterialLotSummaryDto? _selectedMaterialLot;

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
    };

    private static readonly Dictionary<int, string> PageHelpTexts = new()
    {
        [0] = "Elenco dei materiali a magazzino: codice, descrizione, unità di misura, giacenza. Da qui si crea un nuovo materiale, si cerca per codice/descrizione e si importa un catalogo Excel di un fornitore.",
        [1] = "Materiali la cui giacenza è scesa sotto la scorta minima impostata. \"Scansiona\" crea automaticamente una richiesta di materiale mancante per ciascuno di quelli non ancora richiesti.",
        [2] = "Materiali richiesti ma non ancora disponibili: generati automaticamente chiudendo una distinta di prelievo che porta un materiale sotto scorta, oppure dallo scan sottoscorte. Restano aperti finché non arrivano da un ordine fornitore.",
        [3] = "Documenti di prelievo materiale da magazzino verso un'area (es. reparto produzione). Bozza -> Pronta -> Chiusa (scarica davvero la giacenza) oppure Annullata. Solo le distinte in bozza si possono modificare.",
        [4] = "Ordini di acquisto verso i fornitori. Bozza -> Confermato -> ricevuto (anche parzialmente). Ricevere un ordine carica la giacenza e crea un lotto materiale tracciabile per ogni riga.",
        [5] = "Aree/reparti dell'azienda a cui è possibile destinare una distinta di prelievo o assegnare una commessa.",
        [6] = "Utenti abilitati ad accedere al gestionale, con il rispettivo ruolo (Admin, Warehouse, Purchasing, Operator). Solo un Admin può crearne di nuovi.",
        [7] = "Anagrafica dei prodotti che si costruiscono: distinta base (materiali necessari) e ciclo di lavoro (fasi di produzione). Da qui si genera automaticamente la struttura di ogni nuova commessa.",
        [8] = "Commesse di produzione: quantità da costruire di un prodotto, con le fasi del ciclo di lavoro tracciate una per una (avvio/completamento, minuti effettivi, performance). Rilasciare una commessa verifica la disponibilità dei materiali.",
        [9] = "Tracciabilità dei lotti materiale: ogni ingresso di giacenza (ricezione ordine, carico manuale) genera un lotto. Il consumo nelle distinte di prelievo avviene FIFO dal lotto più vecchio; aprendo un lotto si vede dove è stato usato.",
        [10] = "Indicatori aggregati sulle commesse degli ultimi giorni: quante per stato, fasi completate, performance media (minuti stimati/effettivi), percentuale di consegne puntuali. Copre solo la componente \"Performance\", non un OEE completo.",
        [11] = "Anagrafica dei centri di lavoro (reparti/linee) con la loro capacità produttiva giornaliera in minuti, e il confronto con il carico di lavoro attualmente in attesa su ciascuno. È una stima di arretrato, non una pianificazione a calendario con date precise.",
    };

    // TEMPORANEO: login disabilitato su richiesta per velocizzare i test.
    // Per riattivare il login manuale: impostare SkipLoginForTesting a false.
    private const bool SkipLoginForTesting = true;
    private const string TestEmail = "nicolo.test@gestionale.local";
    private const string TestPassword = "Gestionale2026!";

    public MainWindow()
    {
        InitializeComponent();
        NavMaterials.IsChecked = true;
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
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "" : "";
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tagText } radioButton || !int.TryParse(tagText, out var index))
        {
            return;
        }

        MainTabs.SelectedIndex = index;
        PageTitle.Text = PageTitles.GetValueOrDefault(index, string.Empty);
        PageHelpIcon.Text = PageHelpTexts.GetValueOrDefault(index, string.Empty);
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _apiClient.SetBaseUrl(ClientSettings.Load().ApiBaseUrl);
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

    private async Task ConnectAsync()
    {
        try
        {
            ConnectionStatus.Text = "Avvio collegamento...";
            var healthy = await _apiClient.EnsureLocalApiAsync();
            ConnectionStatus.Text = healthy ? "API e Neon online" : "API non disponibile";
            LoginButton.IsEnabled = healthy;

            if (healthy && SkipLoginForTesting)
            {
                try
                {
                    var auth = await _apiClient.LoginAsync(TestEmail, TestPassword);
                    CompleteLogin(auth);
                }
                catch (InvalidOperationException exception)
                {
                    LoginError.Text = $"Auto-login disattivato: {exception.Message}";
                }
            }
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
            CompleteLogin(auth);
        }
        catch (InvalidOperationException exception)
        {
            LoginError.Text = exception.Message;
        }
    }

    private void CompleteLogin(AuthDto auth, bool reloadMaterials = true)
    {
        _apiClient.SetToken(auth.Token);
        _isAuthenticated = true;
        _currentUserId = auth.UserId;
        _currentRole = auth.Role;
        _refreshToken = auth.RefreshToken;
        ScheduleTokenRefresh(auth.ExpiresAt);
        LoginPanel.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Visible;
        ConnectionStatus.Text = $"Online: {auth.Name} ({auth.Role})";
        SidebarSettingsButton.Visibility = auth.Role == "Admin" ? Visibility.Visible : Visibility.Collapsed;
        if (reloadMaterials)
        {
            _ = SearchMaterialsAsync();
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
                await LoadWorkOrdersAsync();
                break;
            case "Lotti materiali" when !_materialLotsLoaded:
                await LoadMaterialLotsAsync();
                break;
            case "Cruscotto" when !_dashboardLoaded:
                await LoadDashboardAsync();
                break;
            case "Centri di lavoro" when !_workCentersLoaded:
                await LoadWorkCentersAsync();
                break;
        }
    }

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

    private async void NewMaterialButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateMaterialWindow(_apiClient) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Created)
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
        if (dialog.ShowDialog() == true && dialog.Created)
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
        if (dialog.ShowDialog() == true && dialog.Created)
        {
            await LoadPurchaseOrdersAsync();
        }
    }

    private void NewSupplierButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateSupplierWindow(_apiClient) { Owner = this };
        dialog.ShowDialog();
    }

    private async void NewAreaButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateAreaWindow(_apiClient) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Created)
        {
            await LoadAreasAsync();
        }
    }

    private async void NewUserButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateUserWindow(_apiClient) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Created)
        {
            await LoadUsersAsync();
        }
    }

    private async void NewProductButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateProductWindow(_apiClient) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Created)
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
            if (dialog.ShowDialog() == true && dialog.Created)
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
        if (dialog.ShowDialog() == true && dialog.Created)
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
            if (dialog.ShowDialog() == true && dialog.Created)
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
        if (dialog.ShowDialog() == true && dialog.Created)
        {
            await LoadMaterialLotsAsync();
        }
    }

    private void MaterialLotsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedMaterialLot = MaterialLotsList.SelectedItem as MaterialLotSummaryDto;
        OpenMaterialLotButton.IsEnabled = _selectedMaterialLot is not null;
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

    private async void RefreshMaterialLotsButton_Click(object sender, RoutedEventArgs e) => await LoadMaterialLotsAsync();

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
            if (dialog.ShowDialog() == true && dialog.Created)
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

    private void PurchaseOrdersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedOrder = PurchaseOrdersList.SelectedItem as PurchaseOrderSummaryDto;
        var hasSelection = _selectedOrder is not null;
        EditOrderButton.IsEnabled = hasSelection && _selectedOrder!.Status == "Draft";
        ConfirmOrderButton.IsEnabled = hasSelection && _selectedOrder!.Status == "Draft";
        ReceiveOrderButton.IsEnabled = hasSelection && (_selectedOrder!.Status is "Confirmed" or "PartiallyReceived");
        CancelOrderButton.IsEnabled = hasSelection && (_selectedOrder!.Status is not ("Received" or "Cancelled"));
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
            if (dialog.ShowDialog() == true && dialog.Created)
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

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            var release = await _updateService.CheckAsync();
            if (release is null)
            {
                MessageBox.Show($"Il programma è aggiornato ({_updateService.CurrentVersion}).", "Aggiornamenti", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"È disponibile la release {release.TagName}.\n\n{release.Notes}\n\nInstallarla ora?",
                "Aggiornamento disponibile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (result == MessageBoxResult.Yes)
            {
                await _updateService.InstallAsync(release);
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

    private Task LoadWorkOrdersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        WorkOrdersList.ItemsSource = await _apiClient.GetWorkOrdersAsync();
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
    });

    private Task LoadDashboardAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var dashboard = await _apiClient.GetWorkOrderDashboardAsync();
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
    });

    private Task LoadWorkCentersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        WorkCentersList.ItemsSource = await _apiClient.GetWorkCentersAsync();
        WorkCenterLoadList.ItemsSource = await _apiClient.GetWorkCenterLoadAsync();
        _workCentersLoaded = true;
    });

    private async void AddWorkCenter_Click(object sender, RoutedEventArgs e)
    {
        var code = WorkCenterCodeBox.Text.Trim();
        var name = WorkCenterNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name) ||
            !decimal.TryParse(WorkCenterCapacityBox.Text, out var capacity) || capacity < 0)
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
