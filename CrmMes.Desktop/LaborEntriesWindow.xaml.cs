using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Hours logged by hand on a work order. Available to every role; deleting an entry is refused
/// by the API for roles below Warehouse, and the message says so.</summary>
public partial class LaborEntriesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;

    public LaborEntriesWindow(ApiClient apiClient, Guid workOrderId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        WorkDatePicker.SelectedDate = DateTime.Today;
        Loaded += async (_, _) => await LoadAsync(loadLookups: true);
    }

    private async Task LoadAsync(bool loadLookups)
    {
        try
        {
            if (loadLookups)
            {
                var order = await _apiClient.GetWorkOrderAsync(_workOrderId);
                HeaderText.Text = $"Ore lavorate · {order.Code}";
                var operations = new List<OperationOption> { new(null, "Nessuna fase") };
                operations.AddRange(order.Operations.OrderBy(op => op.SequenceNumber)
                    .Select(op => new OperationOption(op.Id, $"{op.SequenceNumber}. {op.Name}")));
                OperationCombo.ItemsSource = operations;
                OperationCombo.SelectedIndex = 0;
                var workCenters = await _apiClient.GetWorkCentersAsync();
                WorkCenterCombo.ItemsSource = workCenters;
                // Preselect the work center of the job's phases: hours logged without one can't be
                // valued, and in the first real test every operator entry was left without it.
                var phaseCenters = order.Operations.Select(op => op.WorkCenter).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
                WorkCenterCombo.SelectedItem = workCenters.FirstOrDefault(w => phaseCenters.Any(name =>
                    string.Equals(name, w.Name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, w.Code, StringComparison.OrdinalIgnoreCase)));
            }

            var entries = await _apiClient.GetLaborEntriesAsync(_workOrderId);
            EntriesList.ItemsSource = entries;
            var totalMinutes = entries.Sum(entry => entry.Minutes);
            TotalText.Text = $"Totale ore registrate: {Math.Floor(totalMinutes / 60):0}h {totalMinutes % 60:00}m";
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseDuration(DurationBox.Text, out var minutes))
        {
            ErrorText.Text = "Durata non valida: scrivi le ore come 1,5 oppure 1:30.";
            return;
        }

        var workDate = WorkDatePicker.SelectedDate ?? DateTime.Today;
        var workCenterId = (WorkCenterCombo.SelectedItem as WorkCenterDto)?.Id;
        var operationId = (OperationCombo.SelectedItem as OperationOption)?.Id;
        try
        {
            await _apiClient.AddLaborEntryAsync(
                _workOrderId, minutes, DateTime.SpecifyKind(workDate.Date, DateTimeKind.Utc), workCenterId, operationId,
                string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim());
            ErrorText.Text = string.Empty;
            DurationBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            await LoadAsync(loadLookups: false);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LaborEntryDto entry })
        {
            return;
        }

        if (MessageBox.Show($"Eliminare {entry.HoursLabel} del {entry.WorkDate:dd/MM/yyyy} ({entry.OperatorName})?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.DeleteLaborEntryAsync(_workOrderId, entry.Id);
            await LoadAsync(loadLookups: false);
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>"1:30" is hours:minutes; anything else is decimal hours ("1,5" or "1.5").</summary>
    internal static bool TryParseDuration(string text, out decimal minutes)
    {
        minutes = 0;
        var trimmed = text.Trim();
        if (trimmed.Contains(':'))
        {
            var parts = trimmed.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var hours) && int.TryParse(parts[1], out var mins) &&
                hours >= 0 && mins is >= 0 and < 60)
            {
                minutes = hours * 60 + mins;
                return minutes > 0;
            }

            return false;
        }

        if (NumberInput.TryParseDecimal(trimmed, out var decimalHours) && decimalHours > 0)
        {
            minutes = Math.Round(decimalHours * 60, 1);
            return true;
        }

        return false;
    }

    private sealed record OperationOption(Guid? Id, string Label);
}
