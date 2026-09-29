using System.Globalization;
using System.Windows;

namespace CrmMes.Desktop;

/// <summary>Sets a work center's hourly rate (Admin only on the API side).</summary>
public partial class HourlyRateWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly WorkCenterDto _workCenter;

    public bool Saved { get; private set; }

    public HourlyRateWindow(ApiClient apiClient, WorkCenterDto workCenter)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _workCenter = workCenter;
        HeaderText.Text = $"Tariffa oraria · {workCenter.Name}";
        RateBox.Text = workCenter.HourlyRate?.ToString("0.00", CultureInfo.GetCultureInfo("it-IT")) ?? string.Empty;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        decimal? rate = null;
        if (!string.IsNullOrWhiteSpace(RateBox.Text))
        {
            if (!NumberInput.TryParseDecimal(RateBox.Text, out var parsed) || parsed < 0)
            {
                ErrorText.Text = "Tariffa non valida.";
                return;
            }

            rate = parsed;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SetWorkCenterHourlyRateAsync(_workCenter.Id, rate);
            Saved = true;
            Close();
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
