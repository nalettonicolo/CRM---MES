using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Ingredient name and allergens of a material (food module): they flow into the label of every
/// product whose bill of materials uses it.</summary>
public partial class MaterialFoodInfoWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _materialId;
    private List<AllergenOption> _allergens = [];

    public MaterialFoodInfoWindow(ApiClient apiClient, Guid materialId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _materialId = materialId;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var allergens = _apiClient.GetAllergensAsync();
            var info = _apiClient.GetMaterialFoodInfoAsync(_materialId);
            await Task.WhenAll(allergens, info);
            HeaderText.Text = $"{info.Result.Code} · {info.Result.Name}";
            IngredientBox.Text = info.Result.IngredientName ?? string.Empty;
            _allergens = allergens.Result.Select(a => new AllergenOption(a.Key, a.Name) { IsChecked = info.Result.Allergens.Contains(a.Key) }).ToList();
            AllergenList.ItemsSource = _allergens;
            SaveButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveMaterialFoodInfoAsync(_materialId,
                string.IsNullOrWhiteSpace(IngredientBox.Text) ? null : IngredientBox.Text.Trim(),
                _allergens.Where(a => a.IsChecked).Select(a => a.Key).ToList());
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            SaveButton.IsEnabled = true;
        }
    }

    public sealed class AllergenOption(string key, string name)
    {
        public string Key { get; } = key;
        public string Name { get; } = name;
        public bool IsChecked { get; set; }
    }
}
