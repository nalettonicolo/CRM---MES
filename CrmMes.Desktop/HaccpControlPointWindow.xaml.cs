using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace CrmMes.Desktop;

/// <summary>Creates or edits a critical control point of the HACCP plan.</summary>
public partial class HaccpControlPointWindow : Window
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");
    private readonly ApiClient _apiClient;
    private readonly HaccpControlPointDto? _existing;

    public HaccpControlPointWindow(ApiClient apiClient, HaccpControlPointDto? existing = null)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _existing = existing;
        if (existing is not null)
        {
            HeaderText.Text = existing.Name;
            NameBox.Text = existing.Name;
            LocationBox.Text = existing.Location ?? string.Empty;
            FrequencyBox.Text = existing.Frequency ?? string.Empty;
            HazardBox.Text = existing.Hazard ?? string.Empty;
            MinBox.Text = existing.MinValue?.ToString("0.###", Italian) ?? string.Empty;
            MaxBox.Text = existing.MaxValue?.ToString("0.###", Italian) ?? string.Empty;
            UnitBox.Text = existing.Unit ?? string.Empty;
            ActionBox.Text = existing.CorrectiveActionHint ?? string.Empty;
            ActiveCheck.IsChecked = existing.IsActive;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Indica il nome.";
            return;
        }

        if (!TryReadLimit(MinBox, out var min) || !TryReadLimit(MaxBox, out var max))
        {
            ErrorText.Text = "Limite non valido.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.SaveHaccpControlPointAsync(_existing?.Id, new SaveHaccpControlPointDto(
                NameBox.Text.Trim(), Clean(LocationBox.Text), Clean(HazardBox.Text), Clean(UnitBox.Text), min, max,
                Clean(FrequencyBox.Text), Clean(ActionBox.Text), ActiveCheck.IsChecked == true));
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            SaveButton.IsEnabled = true;
        }
    }

    // Limits may be negative (freezers): parsed as signed numbers, blank means "no limit".
    private static bool TryReadLimit(TextBox box, out decimal? value)
    {
        value = null;
        var text = box.Text.Trim();
        if (text.Length == 0)
        {
            return true;
        }

        var negative = text.StartsWith('-');
        if (!NumberInput.TryParseDecimal(negative ? text[1..] : text, out var parsed))
        {
            return false;
        }

        value = negative ? -parsed : parsed;
        return true;
    }

    private static string? Clean(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
