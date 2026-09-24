using System.Windows;

namespace CrmMes.Desktop;

public partial class UserDetailWindow : Window
{
    public UserDetailWindow(ApiClient apiClient, Guid userId)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                var detail = await apiClient.GetUserDetailAsync(userId);
                Title = $"Utente {detail.Name}";
                UserTitleText.Text = detail.Name;
                UserInfoText.Text = $"{detail.Email} · {detail.Role} · {(detail.IsActive ? "Attivo" : "Inattivo")}";
                AreasPanel.ItemsSource = detail.Areas;
                ActivityList.ItemsSource = detail.RecentActivity;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }
}
