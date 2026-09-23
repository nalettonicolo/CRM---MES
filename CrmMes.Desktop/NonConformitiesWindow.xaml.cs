using System.Globalization;
using System.Windows;

namespace CrmMes.Desktop;

public partial class NonConformitiesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly Guid _operationId;
    private readonly string? _operatorName;

    public NonConformitiesWindow(ApiClient apiClient, Guid workOrderId, Guid operationId, string operationName, string? operatorName = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        _operationId = operationId;
        _operatorName = operatorName;
        OperationNameText.Text = operationName;
        Loaded += NonConformitiesWindow_Loaded;
    }

    private async void NonConformitiesWindow_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        try
        {
            NonConformitiesList.ItemsSource = await _apiClient.GetNonConformitiesAsync(_workOrderId, _operationId);
            await ReloadUnitsAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    /// <summary>Only shows the unit picker when the work order actually has per-serial units (its
    /// quantity was a whole number at creation — see WorkOrderUnit) and at least one is still Pending;
    /// otherwise there's nothing meaningful to scrap by unit and the picker would just be empty.</summary>
    private async Task ReloadUnitsAsync()
    {
        var units = await _apiClient.GetWorkOrderUnitsAsync(_workOrderId);
        var pendingUnits = units.Where(u => u.Status == "Pending").ToList();

        if (pendingUnits.Count == 0)
        {
            UnitPickerPanel.Visibility = Visibility.Collapsed;
            UnitCombo.ItemsSource = null;
            return;
        }

        UnitPickerPanel.Visibility = Visibility.Visible;
        UnitCombo.ItemsSource = pendingUnits;
    }

    private async void RegisterNonConformity_Click(object sender, RoutedEventArgs e)
    {
        var description = DescriptionBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            ErrorText.Text = "Indica la descrizione della non conformità.";
            return;
        }

        if (!decimal.TryParse(ScrapQuantityBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var scrapQuantity) || scrapQuantity <= 0)
        {
            ErrorText.Text = "Indica una quantità scarto maggiore di zero.";
            return;
        }

        var selectedUnitId = (UnitCombo.SelectedItem as WorkOrderUnitDto)?.Id;

        try
        {
            await _apiClient.RegisterNonConformityAsync(_workOrderId, _operationId, description, scrapQuantity,
                string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(), _operatorName, selectedUnitId);
            DescriptionBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            ScrapQuantityBox.Text = "1";
            ErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }
}
