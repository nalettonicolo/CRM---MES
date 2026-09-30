using System.Globalization;
using System.Windows;

namespace CrmMes.Desktop;

public partial class EquipmentDetailWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private static readonly Dictionary<string, string> StateNames = new()
    {
        ["Running"] = "in marcia", ["Idle"] = "in attesa", ["Setup"] = "in attrezzaggio", ["Stopped"] = "ferma",
        ["Alarm"] = "in allarme", ["Off"] = "spenta", ["NoData"] = "senza dati"
    };

    private readonly ApiClient _apiClient;
    private readonly Guid _equipmentId;
    private readonly bool _isAdmin;

    public EquipmentDetailWindow(ApiClient apiClient, Guid equipmentId)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _equipmentId = equipmentId;
        _isAdmin = apiClient.CurrentRole == "Admin";
        Loaded += async (_, _) =>
        {
            try
            {
                var detail = await apiClient.GetEquipmentDetailAsync(equipmentId);
                Title = $"Macchina {detail.Code}";
                EquipmentTitleText.Text = detail.Name;
                EquipmentInfoText.Text =
                    $"Codice {detail.Code} · {(detail.IsActive ? "Attiva" : "Inattiva")}" +
                    (string.IsNullOrWhiteSpace(detail.WorkCenterName) ? "" : $" · Centro di lavoro: {detail.WorkCenterName}");
                TasksList.ItemsSource = detail.Tasks;
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            await LoadMachineAsync();
        };
    }

    private async Task LoadMachineAsync()
    {
        try
        {
            var day = await _apiClient.GetMachineDayAsync(_equipmentId);
            ConnectButton.Visibility = _isAdmin ? Visibility.Visible : Visibility.Collapsed;
            ConnectButton.Content = day.Connected ? "Nuovo token" : "Collega macchina";
            DisconnectButton.Visibility = _isAdmin && day.Connected ? Visibility.Visible : Visibility.Collapsed;
            if (!day.Connected)
            {
                MachineStatusText.Text = "Non collegata. Collegandola, la macchina (o un gateway OPC UA/MQTT) invia stato, pezzi e allarmi: i tempi di marcia e fermo arrivano da soli, senza trascrizioni. Serve un Admin.";
                MachineFigures.Visibility = Visibility.Collapsed;
                LastAlarmsText.Text = string.Empty;
                return;
            }

            var fresh = day.LastSeenAt is { } seen && DateTime.UtcNow - seen < TimeSpan.FromMinutes(15);
            MachineStatusText.Text = day.LastSeenAt is null
                ? $"Collegata (token {day.TokenPrefix}...), in attesa del primo dato dal gateway."
                : $"Ora {(fresh ? StateNames.GetValueOrDefault(day.LastState ?? "", day.LastState ?? "") : "senza dati")} · ultimo dato {day.LastSeenAt.Value.ToLocalTime():dd/MM HH:mm} · token {day.TokenPrefix}...";
            MachineFigures.Visibility = Visibility.Visible;
            AvailabilityText.Text = day.Availability is { } a ? a.ToString("P0", Italian) : "-";
            PiecesText.Text = day.Pieces.ToString("N0", Italian);
            var stopped = day.MinutesByState.Where(p => p.Key is "Stopped" or "Alarm" or "Idle" or "Setup").Sum(p => p.Value);
            RunStopText.Text = $"{Hours(day.MinutesByState.GetValueOrDefault("Running"))} / {Hours(stopped)}";
            AlarmsText.Text = day.AlarmCount.ToString(Italian);
            LastAlarmsText.Text = day.Alarms.Count == 0 ? string.Empty
                : "Ultimi allarmi: " + string.Join(" · ", day.Alarms.Take(5).Select(al => $"{al.Timestamp.ToLocalTime():HH:mm} {al.Code} {al.Text}".Trim()));
        }
        catch (Exception exception)
        {
            MachineStatusText.Text = exception.Message;
        }
    }

    private static string Hours(decimal minutes) => $"{(int)(minutes / 60)}:{(int)(minutes % 60):00}";

    private async void RefreshMachine_Click(object sender, RoutedEventArgs e) => await LoadMachineAsync();

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (DisconnectButton.Visibility == Visibility.Visible &&
            MessageBox.Show("Generare un nuovo token? Quello attuale smette di funzionare: andrà aggiornato sul gateway.", "Nuovo token",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            var token = await _apiClient.CreateMachineTokenAsync(_equipmentId);
            new MachineTokenWindow(token, _equipmentId) { Owner = this }.ShowDialog();
            await LoadMachineAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Collegamento macchina", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Scollegare la macchina? Il gateway non potrà più inviare dati; lo storico resta.", "Scollega",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _apiClient.RevokeMachineTokenAsync(_equipmentId);
            await LoadMachineAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Scollega", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
