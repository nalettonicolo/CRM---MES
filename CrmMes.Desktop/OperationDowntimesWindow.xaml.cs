using System.Windows;

namespace CrmMes.Desktop;

public partial class OperationDowntimesWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly Guid _workOrderId;
    private readonly Guid _operationId;
    private readonly string? _operatorName;
    private readonly Guid? _operatorId;
    private OperationDowntimeDto? _openDowntime;

    public OperationDowntimesWindow(ApiClient apiClient, Guid workOrderId, Guid operationId, string operationName, string? operatorName = null, Guid? operatorId = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workOrderId = workOrderId;
        _operationId = operationId;
        _operatorName = operatorName;
        _operatorId = operatorId;
        OperationNameText.Text = operationName;
        Loaded += OperationDowntimesWindow_Loaded;
    }

    private async void OperationDowntimesWindow_Loaded(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async Task ReloadAsync()
    {
        try
        {
            var downtimes = await _apiClient.GetOperationDowntimesAsync(_workOrderId, _operationId);
            DowntimesList.ItemsSource = downtimes;

            _openDowntime = downtimes.FirstOrDefault(d => d.EndedAt is null);
            if (_openDowntime is not null)
            {
                OpenDowntimePanel.Visibility = Visibility.Visible;
                OpenDowntimeText.Text = $"Fermo aperto dalle {_openDowntime.StartedAt.ToLocalTime():t}: {_openDowntime.Reason}";
                NewDowntimePanel.IsEnabled = false;
            }
            else
            {
                OpenDowntimePanel.Visibility = Visibility.Collapsed;
                NewDowntimePanel.IsEnabled = true;
            }
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void StartDowntime_Click(object sender, RoutedEventArgs e)
    {
        var reason = ReasonBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            ErrorText.Text = "Indica il motivo del fermo.";
            return;
        }

        try
        {
            await _apiClient.StartDowntimeAsync(_workOrderId, _operationId, reason, string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(), _operatorName, _operatorId);
            ReasonBox.Text = string.Empty;
            NotesBox.Text = string.Empty;
            ErrorText.Text = string.Empty;
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }

    private async void EndDowntime_Click(object sender, RoutedEventArgs e)
    {
        if (_openDowntime is null)
        {
            return;
        }

        try
        {
            await _apiClient.EndDowntimeAsync(_workOrderId, _operationId, _openDowntime.Id, _operatorName, _operatorId);
            await ReloadAsync();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
    }
}
