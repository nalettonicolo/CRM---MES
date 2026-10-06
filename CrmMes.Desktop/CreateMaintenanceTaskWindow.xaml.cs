using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

public partial class CreateMaintenanceTaskWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    /// <summary>Campi come li ha impostati l'Admin nello strumento Layout (vuoto = valori di default della finestra).</summary>
    private IReadOnlyList<LayoutFieldDto> _layout = [];

    public CreateMaintenanceTaskWindow(ApiClient apiClient, IReadOnlyList<EquipmentDto> equipment)
    {
        InitializeComponent();
        _apiClient = apiClient;
        EquipmentCombo.ItemsSource = equipment;
        Loaded += async (_, _) => await LoadLayoutAsync();
    }

    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "maintenance.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("equipment", EquipmentCombo),
            new FieldBinding("title", TitleBox),
            new FieldBinding("type", TypePanel),
            new FieldBinding("description", DescriptionBox),
            new FieldBinding("dueDate", DueDatePicker),
            new FieldBinding("recurrence", RecurrenceDaysBox),
        ]);
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
        var missing = FormLayoutApplier.Missing(_layout, key => key switch
        {
            "equipment" => EquipmentCombo.SelectedItem is not null,
            "title" => !string.IsNullOrWhiteSpace(TitleBox.Text),
            "type" => true,
            "description" => !string.IsNullOrWhiteSpace(DescriptionBox.Text),
            "dueDate" => DueDatePicker.SelectedDate is not null,
            "recurrence" => !string.IsNullOrWhiteSpace(RecurrenceDaysBox.Text),
            _ => true,
        });
        if (missing.Count > 0)
        {
            ErrorText.Text = "Compila i campi obbligatori: " + string.Join(", ", missing) + ".";
            return;
        }

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
