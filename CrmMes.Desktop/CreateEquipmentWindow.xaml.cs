using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateEquipmentWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    public CreateEquipmentWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += async (_, _) =>
        {
            try
            {
                WorkCenterCombo.ItemsSource = await _apiClient.GetWorkCentersAsync();
            }
            catch (Exception exception)
            {
                ErrorText.Text = exception.Message;
            }
        };
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var code = CodeBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
        {
            ErrorText.Text = "Nome e codice sono obbligatori.";
            return;
        }

        CreateButton.IsEnabled = false;
        try
        {
            var workCenterId = (WorkCenterCombo.SelectedItem as WorkCenterDto)?.Id;
            await _apiClient.CreateEquipmentAsync(name, code, workCenterId);
            Created = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            CreateButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
