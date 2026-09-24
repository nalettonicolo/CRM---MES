using System.Windows;

namespace CrmMes.Desktop;

public partial class CreateMaintenanceTaskWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    public CreateMaintenanceTaskWindow(ApiClient apiClient, IReadOnlyList<EquipmentDto> equipment)
    {
        InitializeComponent();
        _apiClient = apiClient;
        EquipmentCombo.ItemsSource = equipment;
    }

    private void Type_Changed(object sender, RoutedEventArgs e)
    {
        if (PreventivePanel is null)
        {
            return;
        }

        PreventivePanel.Visibility = PreventiveRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (EquipmentCombo.SelectedItem is not EquipmentDto equipment)
        {
            ErrorText.Text = "Seleziona una macchina.";
            return;
        }

        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ErrorText.Text = "Il titolo è obbligatorio.";
            return;
        }

        var type = PreventiveRadio.IsChecked == true ? "Preventiva" : "Correttiva";
        int? recurrenceDays = null;
        if (type == "Preventiva" && int.TryParse(RecurrenceDaysBox.Text, out var days) && days > 0)
        {
            recurrenceDays = days;
        }

        CreateButton.IsEnabled = false;
        try
        {
            await _apiClient.CreateMaintenanceTaskAsync(
                equipment.Id, title,
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                type, type == "Preventiva" ? DueDatePicker.SelectedDate : null, recurrenceDays);
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
