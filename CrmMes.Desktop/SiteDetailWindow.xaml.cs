using System.Windows;

namespace CrmMes.Desktop;

public partial class SiteDetailWindow : Window
{
    public SiteDetailWindow(ApiClient apiClient, Guid siteId)
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            try
            {
                var detail = await apiClient.GetSiteDetailAsync(siteId);
                Title = $"Sede {detail.Code}";
                SiteTitleText.Text = detail.Name;
                SiteInfoText.Text =
                    $"Codice {detail.Code} · {(detail.IsActive ? "Attiva" : "Inattiva")}" +
                    (string.IsNullOrWhiteSpace(detail.Address) ? "" : $" · {detail.Address}");
                AreasList.ItemsSource = detail.Areas;
                WorkCentersList.ItemsSource = detail.WorkCenters;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }
}
