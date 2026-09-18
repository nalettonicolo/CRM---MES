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
    private string? _refreshToken;
    private readonly DispatcherTimer _refreshTimer = new();
    private PurchaseOrderSummaryDto? _selectedOrder;
    private WithdrawalSlipSummaryDto? _selectedSlip;

    private bool _lowStockLoaded;
    private bool _missingLoaded;
    private bool _slipsLoaded;
    private bool _ordersLoaded;
    private bool _areasLoaded;
    private bool _usersLoaded;

    private static readonly Dictionary<int, string> PageTitles = new()
    {
        [0] = "Materiali",
        [1] = "Sotto scorta",
        [2] = "Materiali mancanti",
        [3] = "Distinte di prelievo",
        [4] = "Ordini fornitore",
        [5] = "Aree",
        [6] = "Utenti",
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
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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
        _refreshToken = auth.RefreshToken;
        ScheduleTokenRefresh(auth.ExpiresAt);
        LoginPanel.Visibility = Visibility.Collapsed;
        DashboardPanel.Visibility = Visibility.Visible;
        ConnectionStatus.Text = $"Online: {auth.Name} ({auth.Role})";
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
}
