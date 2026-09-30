using System.Windows;
using System.Windows.Media;

namespace CrmMes.Desktop;

/// <summary>Records one reading of a control point. The outcome is shown as the value is typed; out of
/// limits the corrective action becomes mandatory (the API enforces the same rule).</summary>
public partial class HaccpReadingWindow : Window
{
    private readonly ApiClient _apiClient;
    private readonly HaccpControlPointDto _point;

    public HaccpReadingWindow(ApiClient apiClient, HaccpControlPointDto point)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _point = point;
        HeaderText.Text = point.Name + (point.Location is null ? string.Empty : $" · {point.Location}");
        LimitsText.Text = $"Limiti: {point.LimitsText}" + (point.Frequency is null ? string.Empty : $" · {point.Frequency}");
        ValueLabel.Text = $"Valore misurato ({point.Unit ?? "valore"})";
        ValuePanel.Visibility = point.IsNumeric ? Visibility.Visible : Visibility.Collapsed;
        YesNoPanel.Visibility = point.IsNumeric ? Visibility.Collapsed : Visibility.Visible;
        ActionBox.Text = point.CorrectiveActionHint ?? string.Empty;
        Loaded += (_, _) => (point.IsNumeric ? ValueBox : (UIElement)CompliantRadio).Focus();
    }

    /// <summary>null while undecided (no value yet).</summary>
    private bool? Compliant(out decimal? value)
    {
        value = null;
        if (!_point.IsNumeric)
        {
            return CompliantRadio.IsChecked == true ? true : NonCompliantRadio.IsChecked == true ? false : null;
        }

        var text = ValueBox.Text.Trim();
        var negative = text.StartsWith('-');
        if (!NumberInput.TryParseDecimal(negative ? text[1..] : text, out var parsed))
        {
            return null;
        }

        value = negative ? -parsed : parsed;
        return (_point.MinValue is null || value >= _point.MinValue) && (_point.MaxValue is null || value <= _point.MaxValue);
    }

    private void Input_Changed(object sender, RoutedEventArgs e)
    {
        if (OutcomeText is null)
        {
            return; // fired while the XAML is being built
        }

        var compliant = Compliant(out _);
        OutcomeText.Text = compliant switch { true => "Conforme", false => "Fuori limite: indica l'azione correttiva", null => string.Empty };
        OutcomeText.Foreground = compliant == false ? (Brush)FindResource("DangerBrush") : (Brush)FindResource("TextSecondaryBrush");
        ActionPanel.Visibility = compliant == false ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = string.Empty;
        var compliant = Compliant(out var value);
        if (compliant is null)
        {
            ErrorText.Text = _point.IsNumeric ? "Indica il valore misurato." : "Indica se il controllo è conforme.";
            return;
        }

        if (compliant == false && string.IsNullOrWhiteSpace(ActionBox.Text))
        {
            ErrorText.Text = "Descrivi l'azione correttiva.";
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await _apiClient.AddHaccpReadingAsync(_point.Id, value, _point.IsNumeric ? null : compliant,
                compliant == false ? ActionBox.Text.Trim() : null, string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim());
            DialogResult = true;
        }
        catch (Exception exception)
        {
            ErrorText.Text = exception.Message;
            SaveButton.IsEnabled = true;
        }
    }
}
