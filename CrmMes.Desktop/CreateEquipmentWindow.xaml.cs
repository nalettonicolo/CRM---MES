using System.Windows;
using CrmMes.Desktop.Layout;

namespace CrmMes.Desktop;

public partial class CreateEquipmentWindow : Window
{
    private readonly ApiClient _apiClient;

    public bool Created { get; private set; }

    private IReadOnlyList<LayoutFieldDto> _layout = [];

    public CreateEquipmentWindow(ApiClient apiClient)
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadLayoutAsync();
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

    private async Task LoadLayoutAsync()
    {
        _layout = await FormLayoutApplier.LoadAsync(_apiClient, "equipment.new");
        FormLayoutApplier.Apply(_layout,
        [
            new FieldBinding("name", NameBox),
            new FieldBinding("code", CodeBox),
            new FieldBinding("workCenter", WorkCenterCombo),
        ]);
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var code = CodeBox.Text.Trim();

        var missing = FormLayoutApplier.Missing(_layout, key => key switch
        {
            "name" => !string.IsNullOrWhiteSpace(name),
            "code" => !string.IsNullOrWhiteSpace(code),
            "workCenter" => WorkCenterCombo.SelectedItem is not null,
            _ => true,
        });
        if (missing.Count > 0)
        {
            ErrorText.Text = "Compila i campi obbligatori: " + string.Join(", ", missing) + ".";
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
