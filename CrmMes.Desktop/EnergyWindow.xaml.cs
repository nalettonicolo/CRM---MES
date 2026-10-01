using System.Net.Http;
using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Monitoraggio energetico: progetti di efficientamento con un periodo di riferimento (ex ante) e
/// un periodo successivo (ex post, impostabile in seguito), più il consumo libero di una macchina o di una
/// commessa. I kWh sono sempre calcolati dalle letture che la macchina/gateway già invia, mai scritti a mano.
/// Gestire i progetti è riservato ad Admin e Direzione (la stessa regola dell'API).</summary>
public partial class EnergyWindow : Window
{
    private readonly ApiClient _apiClient;
    private List<EnergyProjectDto> _projects = [];

    private sealed class ProjectRow(EnergyProjectDto source)
    {
        public Guid Id { get; } = source.Id;
        public string EquipmentLabel { get; } = $"{source.EquipmentName} ({source.EquipmentCode})";
        public string Title { get; } = source.Title;
        public string BaselineLabel { get; } = $"{source.BaselineFrom:dd/MM/yyyy} → {source.BaselineTo:dd/MM/yyyy} · {(source.BaselineKwh is null ? "nessuna lettura" : $"{source.BaselineKwh:0.#} kWh")}";
        public string AfterLabel { get; } = source.AfterFrom is null
            ? "da definire"
            : $"{source.AfterFrom:dd/MM/yyyy} → {source.AfterTo:dd/MM/yyyy} · {(source.AfterKwh is null ? "nessuna lettura" : $"{source.AfterKwh:0.#} kWh")}";
        public string SavingsLabel { get; } = source.SavingsPercent is { } percent ? $"{(percent >= 0 ? "-" : "+")}{Math.Abs(percent):0.#}%" : "—";
    }

    public EnergyWindow(ApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        var canManage = apiClient.CurrentRole is "Admin" or "Management";
        AfterPeriodPanel.Visibility = Visibility.Collapsed;
        ((UIElement)NewProjectEquipmentCombo.Parent).Visibility = canManage ? Visibility.Visible : Visibility.Collapsed;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async Task LoadAsync()
    {
        ErrorText.Text = string.Empty;
        try
        {
            _projects = await _apiClient.GetEnergyProjectsAsync();
            ProjectsList.ItemsSource = _projects.Select(p => new ProjectRow(p)).ToList();

            var equipment = await _apiClient.GetEquipmentAsync();
            NewProjectEquipmentCombo.ItemsSource = equipment;
            LookupEquipmentCombo.ItemsSource = equipment;
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void ProjectsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectRow row)
        {
            AfterPeriodPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var project = _projects.Single(p => p.Id == row.Id);
        AfterPeriodTitleText.Text = $"Periodo ex post · {project.Title}";
        AfterFromPicker.SelectedDate = project.AfterFrom;
        AfterToPicker.SelectedDate = project.AfterTo;
        AfterPeriodPanel.Visibility = Visibility.Visible;
    }

    private async void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (NewProjectEquipmentCombo.SelectedItem is not EquipmentDto equipment || string.IsNullOrWhiteSpace(NewProjectTitleBox.Text)
            || NewProjectFromPicker.SelectedDate is not { } from || NewProjectToPicker.SelectedDate is not { } to)
        {
            ErrorText.Text = "Scegli la macchina, il titolo e il periodo di riferimento (ex ante).";
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            await _apiClient.CreateEnergyProjectAsync(equipment.Id, NewProjectTitleBox.Text, null, from, to);
            NewProjectTitleBox.Text = string.Empty;
            await LoadAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void SaveAfterPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectRow row)
        {
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            await _apiClient.SetEnergyAfterPeriodAsync(row.Id, AfterFromPicker.SelectedDate, AfterToPicker.SelectedDate);
            await LoadAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void ClearAfterPeriod_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectRow row)
        {
            return;
        }

        try
        {
            await _apiClient.SetEnergyAfterPeriodAsync(row.Id, null, null);
            await LoadAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (ProjectsList.SelectedItem is not ProjectRow row)
        {
            return;
        }

        if (MessageBox.Show(this, "Eliminare questo progetto di efficientamento?", "Elimina progetto", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteEnergyProjectAsync(row.Id);
            AfterPeriodPanel.Visibility = Visibility.Collapsed;
            await LoadAsync();
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void LookupEquipment_Click(object sender, RoutedEventArgs e)
    {
        if (LookupEquipmentCombo.SelectedItem is not EquipmentDto equipment || LookupFromPicker.SelectedDate is not { } from || LookupToPicker.SelectedDate is not { } to)
        {
            ErrorText.Text = "Scegli la macchina e il periodo.";
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            var result = await _apiClient.GetEquipmentEnergyConsumptionAsync(equipment.Id, from, to);
            LookupResultText.Text = result.TotalKwh is null ? "Nessuna lettura in questo periodo." : $"{result.TotalKwh:0.#} kWh da {result.ReadingCount} letture.";
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void LookupWorkOrder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(WorkOrderCodeBox.Text))
        {
            ErrorText.Text = "Scrivi il codice della commessa.";
            return;
        }

        try
        {
            ErrorText.Text = string.Empty;
            var result = await _apiClient.GetWorkOrderEnergyConsumptionAsync(WorkOrderCodeBox.Text.Trim());
            WorkOrderResultText.Text = result.TotalKwh is null
                ? "Nessuna lettura associata a questa commessa."
                : $"{result.TotalKwh:0.#} kWh ({string.Join(", ", result.ByEquipment.Select(e => $"{e.EquipmentName}: {e.Kwh:0.#} kWh"))})";
        }
        catch (HttpRequestException exception)
        {
            ErrorText.Text = exception.Message;
        }
    }
}
