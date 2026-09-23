using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

public partial class AreaDetailWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _areaId;

    public AreaDetailWindow(ApiClient apiClient, Guid areaId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _areaId = areaId;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            var detail = await _apiClient.GetAreaDetailAsync(_areaId);
            Title = $"Area {detail.Code}";
            AreaTitleText.Text = detail.Name;
            AreaInfoText.Text = $"Codice {detail.Code} · {(detail.IsActive ? "Attiva" : "Inattiva")}";
            UsersList.ItemsSource = detail.Users;
            WorkOrdersList.ItemsSource = detail.WorkOrders;
            WithdrawalSlipsList.ItemsSource = detail.WithdrawalSlips;
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void AssignUser_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var allUsers = await _apiClient.GetUsersAsync();
            var assignedIds = (UsersList.ItemsSource as IEnumerable<AreaUserDto> ?? []).Select(u => u.Id).ToHashSet();
            var candidates = allUsers.Where(u => u.IsActive && !assignedIds.Contains(u.Id)).ToList();
            if (candidates.Count == 0)
            {
                MessageBox.Show("Non ci sono altri utenti attivi da assegnare.", "Assegna utente", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SelectUserWindow(candidates) { Owner = this };
            if (dialog.ShowDialog() == true && dialog.SelectedUserId is Guid userId)
            {
                await _apiClient.AssignUserToAreaAsync(_areaId, userId);
                await ReloadAsync();
            }
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RemoveUser_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AreaUserDto user })
        {
            return;
        }

        if (MessageBox.Show($"Rimuovere {user.Name} da quest'area?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.UnassignUserFromAreaAsync(_areaId, user.Id);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
