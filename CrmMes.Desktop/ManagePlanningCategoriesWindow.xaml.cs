using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CrmMes.Desktop;

public partial class ManagePlanningCategoriesWindow : Window
{
    private readonly ApiClient _apiClient;

    private static readonly (string Name, string Hex)[] Palette =
    [
        ("Arancio", "#C57821"),
        ("Blu", "#2E6F9E"),
        ("Verde", "#3D7A4C"),
        ("Viola", "#7B4FA3"),
        ("Rosso", "#C0392B"),
        ("Grigio", "#8C7F6A"),
        ("Ambra", "#B36F1B"),
        ("Turchese", "#1F8A8C"),
    ];

    public ManagePlanningCategoriesWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;

        foreach (var (name, hex) in Palette)
        {
            NewColorCombo.Items.Add(new ComboBoxItem
            {
                Content = name,
                Tag = hex,
                Background = (Brush)new BrushConverter().ConvertFromString(hex)!,
                Foreground = Brushes.White,
            });
        }
        NewColorCombo.SelectedIndex = 0;

        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            CategoriesList.ItemsSource = await _apiClient.GetPlanningCategoriesAsync(includeInactive: true);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        var code = NewCodeBox.Text.Trim();
        var name = NewNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Codice e nome sono obbligatori.";
            return;
        }

        var colorHex = (NewColorCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "#8C7F6A";

        try
        {
            await _apiClient.CreatePlanningCategoryAsync(code, name, colorHex);
            NewCodeBox.Text = "";
            NewNameBox.Text = "";
            ErrorText.Text = "";
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void DeactivateCategory_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not PlanningCategoryDto category)
        {
            return;
        }

        if (MessageBox.Show(
                $"Disattivare la categoria \"{category.Name}\"? Le settimane già dipinte con questa categoria restano visibili nello storico, ma non sarà più selezionabile per nuove modifiche.",
                "Conferma disattivazione", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeactivatePlanningCategoryAsync(category.Id);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
