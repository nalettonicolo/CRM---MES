using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CrmMes.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ApiClient _apiClient = new();
    private readonly UpdateService _updateService = new();
    private bool _isAuthenticated;
    private PurchaseOrderSummaryDto? _selectedOrder;

    private bool _lowStockLoaded;
    private bool _missingLoaded;
    private bool _slipsLoaded;
    private bool _ordersLoaded;
    private bool _areasLoaded;
    private bool _usersLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ConnectionStatus.Text = "Avvio collegamento...";
            var healthy = await _apiClient.EnsureLocalApiAsync();
            ConnectionStatus.Text = healthy ? "API e Neon online" : "API non disponibile";
            LoginButton.IsEnabled = healthy;
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
            _apiClient.SetToken(auth.Token);
            _isAuthenticated = true;
            LoginPanel.Visibility = Visibility.Collapsed;
            DashboardPanel.Visibility = Visibility.Visible;
            ConnectionStatus.Text = $"Online: {auth.Name} ({auth.Role})";
            await SearchMaterialsAsync();
        }
        catch (InvalidOperationException exception)
        {
            LoginError.Text = exception.Message;
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
        ConfirmOrderButton.IsEnabled = hasSelection && _selectedOrder!.Status == "Draft";
        ReceiveOrderButton.IsEnabled = hasSelection && (_selectedOrder!.Status is "Confirmed" or "PartiallyReceived");
        CancelOrderButton.IsEnabled = hasSelection && (_selectedOrder!.Status is not ("Received" or "Cancelled"));
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
    });

    private Task LoadPurchaseOrdersAsync() => RunBusyAsync(string.Empty, async () =>
    {
        var orders = await _apiClient.GetPurchaseOrdersAsync();
        PurchaseOrdersList.ItemsSource = orders;
        _ordersLoaded = true;
        _selectedOrder = null;
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
